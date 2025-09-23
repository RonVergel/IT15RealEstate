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

namespace RealEstateCRM.Controllers
{
    [Authorize(Roles = "Broker")] // only Brokers can use this controller
    public class AdminController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _role_manager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            ILogger<AdminController> logger)
        {
            _userManager = userManager;
            _role_manager = roleManager; // kept name consistent below
            _emailSender = emailSender;
            _logger = logger;
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
                // Skip users who are also Brokers — show only pure Agents in Manage Agents
                var isBroker = await _userManager.IsInRoleAsync(u, "Broker");
                if (isBroker) continue;

                model.Add(new AgentViewModel
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Email = u.Email,
                    EmailConfirmed = u.EmailConfirmed,
                    LockoutEnd = u.LockoutEnd,
                    IsBroker = false
                });
            }

            return View(model);
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
        public async Task<IActionResult> CreateAgent([FromForm] CreateAgentRequest model, string returnUrl = "/Dashboard")
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

            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 20px auto; padding: 20px; border: 1px solid #ddd; border-radius: 10px;'>
                        <h2 style='text-align: center; color: #0056b3;'>Welcome to Real Estate CRM!</h2>
                        <p>An admin has created an account for you. Please confirm your email address to activate the account.</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style='background-color: #0056b3; color: white; padding: 15px 25px; text-decoration: none; border-radius: 5px; font-size: 16px;'>Confirm Your Account</a>
                        </div>
                        <p>If you are having trouble with the button above, please copy and paste the URL below into your web browser:</p>
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
    }
}