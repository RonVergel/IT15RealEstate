using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.WebUtilities;
using System.Linq; // added for LINQ
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using System.IO;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;

namespace RealEstateCRM.Controllers
{
    [Authorize(Roles = "Broker")] // only Brokers can use this controller
    public class AdminController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _role_manager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AdminController> _logger;
        private readonly AppDbContext _context;

        public AdminController(
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            ILogger<AdminController> logger,
            AppDbContext context)
        {
            _userManager = userManager;
            _role_manager = roleManager; // kept name consistent below
            _emailSender = emailSender;
            _logger = logger;
            _context = context;
        }

        // GET: /Admin
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Ensure role exists
            if (!await _role_manager.RoleExistsAsync("Agent"))
            {
                // No agents yet - return empty list to view
                return View(new List<AgentViewModel>());
            }

            // Get all users in the "Agent" role
            var agentUsers = await _userManager.GetUsersInRoleAsync("Agent");

            var model = new List<AgentViewModel>();
            foreach (var u in agentUsers)
            {
                // Skip users who are also Brokers - show only pure Agents in Manage Agents
                var isBroker = await _userManager.IsInRoleAsync(u, "Broker");
                if (isBroker) continue;

                // Try to read FullName claim
                string? fullName = null;
                string? avatar = null;
                try
                {
                    var claims = await _userManager.GetClaimsAsync(u);
                    fullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value;
                    avatar = claims.FirstOrDefault(c => c.Type == "AvatarUrl")?.Value;
                }
                catch { }

                model.Add(new AgentViewModel
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Email = u.Email,
                    EmailConfirmed = u.EmailConfirmed,
                    LockoutEnd = u.LockoutEnd,
                    IsBroker = false,
                    FullName = fullName,
                    PhoneNumber = u.PhoneNumber,
                    AvatarUrl = avatar
                });
            }

            return View(model);
        }

        // Quick JSON summary for a given agent
        [HttpGet]
        public async Task<IActionResult> AgentSummary(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            string displayName = user.UserName ?? user.Email ?? string.Empty;
            string? fullName = null; string? avatar = null;
            try
            {
                var claims = await _userManager.GetClaimsAsync(user);
                fullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value;
                avatar = claims.FirstOrDefault(c => c.Type == "AvatarUrl")?.Value;
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(fullName)) displayName = fullName;

            // Aggregate deals assigned to this agent (match on AgentName)
            var dealsQuery = _context.Deals.Include(d => d.Property)
                .Where(d => d.AgentName != null && (EF.Functions.ILike(d.AgentName!, displayName) || EF.Functions.ILike(d.AgentName!, (user.UserName ?? string.Empty)) || EF.Functions.ILike(d.AgentName!, (user.Email ?? string.Empty))));

            var byStatus = await dealsQuery
                .GroupBy(d => d.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int get(string s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            var total = byStatus.Sum(x => x.Count);

            var recentDeals = await dealsQuery
                .OrderByDescending(d => d.LastUpdated)
                .Take(5)
                .Select(d => new {
                    id = d.Id,
                    title = d.Title,
                    property = d.Property != null ? d.Property.Title : "",
                    price = d.Property != null ? d.Property.Price : 0,
                    offerAmount = d.OfferAmount,
                    status = d.Status,
                    lastUpdated = d.LastUpdated
                })
                .ToListAsync();

            var payload = new {
                id = user.Id,
                name = displayName,
                email = user.Email,
                phone = user.PhoneNumber,
                emailConfirmed = user.EmailConfirmed,
                lockoutEnd = user.LockoutEnd,
                avatarUrl = avatar,
                deals = new {
                    total,
                    newCount = get("New"),
                    offerMade = get("OfferMade"),
                    negotiation = get("Negotiation"),
                    contractDraft = get("ContractDraft"),
                    closed = get("Closed"),
                    recent = recentDeals
                }
            };

            return Json(payload);
        }

        // Manage actions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LockAgent(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
            await _userManager.UpdateAsync(user);
            // Force current sessions to revalidate and sign out
            try { await _userManager.UpdateSecurityStampAsync(user); } catch { }
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlockAgent(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            user.LockoutEnd = null;
            await _userManager.UpdateAsync(user);
            // Optionally rotate stamp to ensure clean sign-in next request
            try { await _userManager.UpdateSecurityStampAsync(user); } catch { }
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendConfirmation(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            if (string.IsNullOrWhiteSpace(user.Email)) return BadRequest("User has no email.");

            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId = user.Id, code = code, returnUrl = "/Dashboard" },
                protocol: Request.Scheme);

            var body = $@"<p>Please confirm your account for Real Estate CRM:</p><p><a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>Confirm Email</a></p>";
            await _emailSender.SendEmailAsync(user.Email, "Confirm your email", body);
            return Ok();
        }

        // Send a custom email to an agent from Manage Agents
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmailAgent(string id, string subject, string message)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("Missing agent id.");
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            if (string.IsNullOrWhiteSpace(user.Email)) return BadRequest("User has no email address.");

            subject = (subject ?? string.Empty).Trim();
            message = (message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(subject)) return BadRequest("Subject is required.");
            if (string.IsNullOrWhiteSpace(message)) return BadRequest("Message is required.");

            // Basic HTML wrapper for readability
            var body = $@"<div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                <p>{System.Net.WebUtility.HtmlEncode(message).Replace("\n", "<br/>")}</p>
                <hr style='border:none;border-top:1px solid #eee;margin:16px 0'/>
                <p style='font-size:12px;color:#888;'>Sent via Real Estate CRM • {DateTimeOffset.Now.LocalDateTime:g}</p>
            </div>";

            try
            {
                await _emailSender.SendEmailAsync(user.Email!, subject, body);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to email agent {Email}", user.Email);
                return StatusCode(500, "Failed to send email. Please try again later.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAgent(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // Don't allow deleting brokers
            if (await _userManager.IsInRoleAsync(user, "Broker")) return BadRequest("Cannot delete a Broker.");

            // Prevent deletion if user is referenced as Agent on any deal
            string display = user.UserName ?? user.Email ?? string.Empty;
            try
            {
                var claims = await _userManager.GetClaimsAsync(user);
                var full = claims.FirstOrDefault(c => c.Type == "FullName")?.Value;
                if (!string.IsNullOrWhiteSpace(full)) display = full;
            }
            catch { }

            var hasDeals = await _context.Deals.AnyAsync(d => d.AgentName != null &&
                (d.AgentName == display || d.AgentName == user.UserName || d.AgentName == user.Email));
            if (hasDeals) return BadRequest("Cannot delete agent with existing deals.");

            await _userManager.DeleteAsync(user);
            return Ok();
        }

        // Promote an existing user (by email) to Broker
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PromoteToBroker(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Email is required.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _role_manager.RoleExistsAsync("Broker"))
            {
                await _role_manager.CreateAsync(new IdentityRole("Broker"));
            }

            if (await _userManager.IsInRoleAsync(user, "Broker"))
            {
                TempData["InfoMessage"] = "User is already a Broker.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.AddToRoleAsync(user, "Broker");
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "User promoted to Broker.";
                _logger.LogInformation("User {Email} promoted to Broker by {Actor}.", email, User.Identity?.Name);
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = "Failed to promote user: " + string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        // Revoke Broker role from an existing user
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeBroker(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Email is required.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _userManager.IsInRoleAsync(user, "Broker"))
            {
                TempData["InfoMessage"] = "User is not a Broker.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.RemoveFromRoleAsync(user, "Broker");
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Broker role removed from user.";
                _logger.LogInformation("Broker role removed from {Email} by {Actor}.", email, User.Identity?.Name);
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = "Failed to remove Broker role: " + string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        // Create an agent account (used by admin modal)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAgent([FromForm] CreateAgentRequest model, IFormFile? Photo, string returnUrl = "/Dashboard")
        {
            if (!ModelState.IsValid)
            {
                // preserve simple feedback path for the UI — return to dashboard with errors
                TempData["ErrorMessage"] = "Invalid input. Please check the form.";
                return LocalRedirect(returnUrl);
            }

            // Ensure passwords match
            if (model.Password != model.ConfirmPassword)
            {
                TempData["ErrorMessage"] = "Password and confirmation do not match.";
                return LocalRedirect(returnUrl);
            }

            var existing = await _userManager.FindByEmailAsync(model.Email?.Trim() ?? string.Empty);
            if (existing != null)
            {
                TempData["ErrorMessage"] = "A user with that email already exists.";
                return LocalRedirect(returnUrl);
            }

            var user = new IdentityUser
            {
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = false
            };

            var createResult = await _userManager.CreateAsync(user, model.Password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                TempData["ErrorMessage"] = "Failed to create user: " + errors;
                return LocalRedirect(returnUrl);
            }

            // Optional: store phone and full name without schema changes
            if (!string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                user.PhoneNumber = model.PhoneNumber.Trim();
                await _userManager.UpdateAsync(user);
            }
            if (!string.IsNullOrWhiteSpace(model.Name))
            {
                await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("FullName", model.Name.Trim()));
            }

            // Optional: handle agent photo upload and store as claim AvatarUrl
            try
            {
                if (Photo != null && Photo.Length > 0)
                {
                    var allowed = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
                    if (allowed.Contains(Photo.ContentType) && Photo.Length <= 5 * 1024 * 1024)
                    {
                        var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "agents");
                        if (!Directory.Exists(uploadsRoot)) Directory.CreateDirectory(uploadsRoot);
                        var ext = Path.GetExtension(Photo.FileName);
                        if (string.IsNullOrWhiteSpace(ext))
                        {
                            // fallback by content type
                            ext = Photo.ContentType switch
                            {
                                "image/jpeg" => ".jpg",
                                "image/png" => ".png",
                                "image/gif" => ".gif",
                                "image/webp" => ".webp",
                                _ => ".img"
                            };
                        }
                        var fileName = $"agent_{Guid.NewGuid():N}{ext}";
                        var filePath = Path.Combine(uploadsRoot, fileName);
                        using (var stream = System.IO.File.Create(filePath))
                        {
                            await Photo.CopyToAsync(stream);
                        }
                        var relative = $"/uploads/agents/{fileName}";
                        await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("AvatarUrl", relative));
                    }
                }
            }
            catch { }

            // Ensure Agent role exists and add user to it
            if (!await _role_manager.RoleExistsAsync("Agent"))
            {
                await _role_manager.CreateAsync(new IdentityRole("Agent"));
            }

            var addToRoleResult = await _userManager.AddToRoleAsync(user, "Agent");
            if (!addToRoleResult.Succeeded)
            {
                // Rollback if role assignment fails
                await _userManager.DeleteAsync(user);
                TempData["ErrorMessage"] = "Failed to assign role: " + string.Join("; ", addToRoleResult.Errors.Select(e => e.Description));
                return LocalRedirect(returnUrl);
            }

            // Generate email confirmation token and send email (same flow as Register page)
            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                protocol: Request.Scheme);
            var loginUrl = Url.Page(
                "/Account/Login",
                pageHandler: null,
                values: new { area = "Identity", returnUrl = returnUrl },
                protocol: Request.Scheme);

            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 20px auto; padding: 20px; border: 1px solid #ddd; border-radius: 10px;'>
                        <h2 style='text-align: center; color: #0056b3;'>Welcome to Real Estate CRM!</h2>\r\n                        <p>An admin has created an account for you. Please confirm your email address to activate the account.</p>\r\n\r\n                        <h3 style='margin-top: 24px; margin-bottom: 8px;'>Your Login Details</h3>\r\n                        <ul style='padding-left: 18px; margin-top: 0;'>\r\n                            <li>Email: <strong>{HtmlEncoder.Default.Encode(model.Email)}</strong></li>\r\n                            <li>Temporary Password: <strong>{HtmlEncoder.Default.Encode(model.Password)}</strong></li>\r\n                        </ul>\r\n                        <p style='font-size: 12px; color: #666; margin-top: 6px;'>For your security, please change your password after your first sign-in.</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style='background-color: #0056b3; color: white; padding: 15px 25px; text-decoration: none; border-radius: 5px; font-size: 16px;'>Confirm Your Account</a>
                        </div>\r\n                        <div style='text-align: center; margin: 10px 0;'>\r\n                            <a href='{HtmlEncoder.Default.Encode(loginUrl)}' style='color: #0056b3; text-decoration: underline;'>Go to Login</a>\r\n                        </div>\r\n                        <p>If you are having trouble with the button above, please copy and paste the URL below into your web browser:</p>
                        <p style='word-break: break-all; font-size: 12px;'><a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>{HtmlEncoder.Default.Encode(callbackUrl)}</a></p>
                        <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'/>
                        <p style='font-size: 12px; color: #888;'>If you did not expect this account, please contact your administrator.</p>
                    </div>
                </div>";

            try
            {
                await _emailSender.SendEmailAsync(model.Email, "Confirm your email for Real Estate CRM", emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email for new agent {Email}", model.Email);
                // do not fail the flow if email sending fails; user was created
            }

            TempData["SuccessMessage"] = "Agent account created. A confirmation email was sent to the new user.";
            return LocalRedirect(returnUrl);
        }
    }

    // DTO used by CreateAgent action
    public class CreateAgentRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Phone]
        [System.ComponentModel.DataAnnotations.Display(Name = "Phone Number")]
        public string? PhoneNumber { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.EmailAddress]
        public string Email { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 6)]
        [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Password)]
        [System.ComponentModel.DataAnnotations.Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // Simple view model for listing agents
    public class AgentViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public System.DateTimeOffset? LockoutEnd { get; set; }

        // New: indicates whether this user is also a Broker
        public bool IsBroker { get; set; }

        // New: preferred display name and phone
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }

        // New: avatar url (claim stored)
        public string? AvatarUrl { get; set; }
    }
}



