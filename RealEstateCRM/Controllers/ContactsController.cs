using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using RealEstateCRM.Services.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System.Text.Encodings.Web;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public partial class ContactsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notifications;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ContactsController> _logger;

        public ContactsController(AppDbContext db,
                                  INotificationService notifications,
                                  UserManager<IdentityUser> userManager,
                                  IEmailSender emailSender,
                                  ILogger<ContactsController> logger)
        {
            _db = db;
            _notifications = notifications;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        // GET: /Contacts/Index?page=1 - Exclude contacts with Type = "Lead" (paged)
        public async Task<IActionResult> Index(int page = 1)
        {
            const int pageSize = 10;
            try
            {
                var totalCount = await _db.Contacts.Where(c => c.IsActive && c.Type != "Lead").CountAsync();
                var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
                page = Math.Max(1, Math.Min(page, totalPages));

                var contacts = await _db.Contacts
                    .Where(c => c.IsActive && c.Type != "Lead")
                    .OrderByDescending(c => c.DateCreated)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalCount = totalCount;

                return View(contacts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading contacts page {Page}", page);
                ViewBag.CurrentPage = 1;
                ViewBag.TotalPages = 1;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalCount = 0;
                return View(new List<Contact>());
            }
        }

        // GET: /Contacts/GetContact?id=123
        [HttpGet]
        public async Task<IActionResult> GetContact(int id)
        {
            try
            {
                var c = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
                if (c == null)
                {
                    return Json(new { success = false, message = "Contact not found" });
                }
                return Json(new
                {
                    success = true,
                    id = c.Id,
                    name = c.Name,
                    email = c.Email,
                    phone = c.Phone,
                    type = c.Type,
                    agent = c.Agent,
                    // No Address field on Contact model; return empty for UI placeholder
                    address = "",
                    occupation = c.Occupation,
                    salary = c.Salary,
                    notes = c.Notes,
                    dateCreated = c.DateCreated.ToString("MMM dd, yyyy"),
                    lastContacted = c.LastContacted?.ToString("MMM dd, yyyy")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting contact {Id}", id);
                return Json(new { success = false, message = "An error occurred while retrieving the contact" });
            }
        }

        // POST: /Contacts/SendEmail
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendEmail(int contactId, string subject, string body, List<IFormFile>? files)
        {
            try
            {
                var c = await _db.Contacts.FindAsync(contactId);
                if (c == null || !c.IsActive)
                {
                    return Json(new { success = false, message = "Contact not found or inactive." });
                }
                if (string.IsNullOrWhiteSpace(c.Email))
                {
                    return Json(new { success = false, message = "This contact has no email address." });
                }
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
                {
                    return Json(new { success = false, message = "Subject and message are required." });
                }

                // Save attachments to contact documents (if provided)
                var uploaded = new List<(string Name, string Url)>();
                if (files != null && files.Count > 0)
                {
                    var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "contacts", contactId.ToString());
                    Directory.CreateDirectory(uploadsRoot);
                    const long maxBytes = 20 * 1024 * 1024;
                    foreach (var f in files)
                    {
                        if (f == null || f.Length == 0) continue;
                        if (f.Length > maxBytes) continue;
                        var safeName = string.Join("_", f.FileName.Split(Path.GetInvalidFileNameChars()));
                        var unique = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0,8) + "_" + safeName;
                        var full = Path.Combine(uploadsRoot, unique);
                        using (var stream = System.IO.File.Create(full))
                        {
                            await f.CopyToAsync(stream);
                        }
                        var url = $"/uploads/contacts/{contactId}/{unique}";
                        uploaded.Add((unique, url));
                    }
                }

                // Compose HTML body with links to uploaded files
                var encodedBody = WebUtility.HtmlEncode(body).Replace("\n", "<br/>");
                string attachmentsHtml = string.Empty;
                if (uploaded.Any())
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in uploaded)
                    {
                        var safeUrl = HtmlEncoder.Default.Encode(item.Url);
                        var safeName = HtmlEncoder.Default.Encode(item.Name);
                        sb.Append("<li><a href='").Append(safeUrl).Append("'>").Append(safeName).Append("</a></li>");
                    }
                    attachmentsHtml = "<hr/><div style='margin-top:8px'><div style='font-weight:600'>Attached Files</div><ul>" + sb.ToString() + "</ul></div>";
                }

                var htmlBody = "<div style='font-family:Arial,sans-serif;color:#333;line-height:1.5'>" + encodedBody + attachmentsHtml + "</div>";
                await _emailSender.SendEmailAsync(c.Email!, subject.Trim(), htmlBody);

                // Update LastContacted
                c.LastContacted = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Email sent.", lastContacted = c.LastContacted?.ToString("MMM dd, yyyy") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to contact {Id}", contactId);
                return Json(new { success = false, message = "Failed to send email. Please try again." });
            }
        }

        // GET: /Contacts/ListDocuments?contactId=123
        [HttpGet]
        public IActionResult ListDocuments(int contactId)
        {
            try
            {
                if (contactId <= 0) return Json(new { success = false, message = "Invalid contact id" });
                var root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "contacts", contactId.ToString());
                if (!Directory.Exists(root)) return Json(new { success = true, files = Array.Empty<object>() });
                var files = Directory.GetFiles(root)
                    .OrderByDescending(p => System.IO.File.GetCreationTimeUtc(p))
                    .Select(p => new
                    {
                        name = Path.GetFileName(p),
                        size = new FileInfo(p).Length,
                        uploadedAt = System.IO.File.GetCreationTimeUtc(p),
                        url = "/uploads/contacts/" + contactId + "/" + Path.GetFileName(p)
                    })
                    .ToList();
                return Json(new { success = true, files });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing documents for contact {Id}", contactId);
                return Json(new { success = false, message = "Failed to list documents." });
            }
        }

        // POST: /Contacts/UploadDocument
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocument(int contactId, IFormFile file)
        {
            try
            {
                if (contactId <= 0) return Json(new { success = false, message = "Invalid contact id" });
                if (file == null || file.Length == 0) return Json(new { success = false, message = "No file uploaded." });

                var contact = await _db.Contacts.FindAsync(contactId);
                if (contact == null || !contact.IsActive) return Json(new { success = false, message = "Contact not found or inactive." });

                // Limit size to 20 MB
                const long maxBytes = 20 * 1024 * 1024;
                if (file.Length > maxBytes) return Json(new { success = false, message = "File exceeds 20MB limit." });

                var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "contacts", contactId.ToString());
                Directory.CreateDirectory(uploadsRoot);

                var safeFileName = string.Join("_", file.FileName.Split(Path.GetInvalidFileNameChars()));
                var uniqueName = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + safeFileName;
                var fullPath = Path.Combine(uploadsRoot, uniqueName);
                using (var stream = System.IO.File.Create(fullPath))
                {
                    await file.CopyToAsync(stream);
                }

                var relUrl = $"/uploads/contacts/{contactId}/{uniqueName}";
                return Json(new { success = true, file = new { name = uniqueName, size = file.Length, url = relUrl, uploadedAt = DateTime.UtcNow } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload document for contact {Id}", contactId);
                return Json(new { success = false, message = "Failed to upload document." });
            }
        }

        // POST: /Contacts/CreateContact
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateContact(Contact contact)
        {
            const int pageSize = 10;
            if (ModelState.IsValid)
            {
                try
                {
                    contact.DateCreated = DateTime.UtcNow;
                    contact.IsActive = true;

                    _db.Contacts.Add(contact);
                    var saveResult = await _db.SaveChangesAsync();

                    if (saveResult > 0)
                    {
                        _logger.LogInformation($"Contact '{contact.Name}' created successfully with ID {contact.Id} and Type '{contact.Type}'");

                        // Set success message in TempData
                        TempData["SuccessMessage"] = "Contact Added";

                        return RedirectToAction(nameof(Index));
                    }
                    else
                    {
                        _logger.LogWarning($"No changes were saved when creating contact '{contact.Name}'");
                        ModelState.AddModelError("", "No changes were saved. Please try again.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error creating contact '{contact.Name}'");
                    ModelState.AddModelError("", "An error occurred while saving the contact. Please try again.");
                }
            }
            else
            {
                _logger.LogWarning("Model validation failed when creating contact");
                foreach (var modelError in ModelState.Values.SelectMany(v => v.Errors))
                {
                    _logger.LogWarning($"Validation error: {modelError.ErrorMessage}");
                }
            }

            // If we got this far, something failed — display first page with validation errors
            var totalCount = await _db.Contacts.Where(c => c.IsActive && c.Type != "Lead").CountAsync();
            var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var page = 1;
            var contacts = await _db.Contacts
                .Where(c => c.IsActive && c.Type != "Lead")
                .OrderByDescending(c => c.DateCreated)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;

            return View("Index", contacts);
        }

        // POST: /Contacts/UpdateContactType
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateContactType(int contactId, string newType)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _logger.LogInformation($"Attempting to update contact {contactId} type to '{newType}'");

                var contact = await _db.Contacts.FindAsync(contactId);
                if (contact == null)
                {
                    _logger.LogWarning($"Contact with ID {contactId} not found");
                    return Json(new { success = false, message = "Contact not found" });
                }

                if (!contact.IsActive)
                {
                    _logger.LogWarning($"Contact with ID {contactId} is inactive");
                    return Json(new { success = false, message = "Contact is inactive" });
                }

                // Validate the new type
                var validTypes = new[] { "Agent", "Client", "Lead" };
                if (!validTypes.Contains(newType))
                {
                    _logger.LogWarning($"Invalid contact type '{newType}' for contact {contactId}");
                    return Json(new { success = false, message = "Invalid contact type" });
                }

                // Check if the type is actually changing
                if (contact.Type == newType)
                {
                    _logger.LogInformation($"Contact {contactId} already has type '{newType}', no change needed");
                    return Json(new { success = true, message = "Contact type is already set to this value" });
                }

                var oldType = contact.Type;

                // Special handling for converting to Lead - move to Leads table
                if (newType == "Lead")
                {
                    // Create new lead from contact data (include Occupation and Salary)
                    var lead = new Lead
                    {
                        Name = contact.Name,
                        Agent = contact.Agent,
                        Email = contact.Email,
                        Phone = contact.Phone,
                        DateCreated = contact.DateCreated,
                        LastContacted = contact.LastContacted,
                        Notes = contact.Notes,
                        IsActive = true,
                        OriginalContactId = contact.Id,
                        LeadSource = "Converted",
                        // Preserve occupation and salary when converting to Lead
                        Occupation = contact.Occupation,
                        Salary = contact.Salary
                    };

                    // Add the lead to the Leads table
                    _db.Leads.Add(lead);

                    // Remove the contact from the Contacts table
                    _db.Contacts.Remove(contact);

                    var saveResult = await _db.SaveChangesAsync();

                    if (saveResult > 0)
                    {
                        await transaction.CommitAsync();
                        _logger.LogInformation($"Successfully converted contact '{contact.Name}' (ID: {contactId}) to lead (ID: {lead.Id})");

                        return Json(new {
                            success = true,
                            message = $"Contact '{contact.Name}' has been converted to a lead",
                            leadId = lead.Id,
                            shouldRedirect = true,
                            redirectUrl = "/Leads/Index"
                        });
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError($"No changes were saved when converting contact {contactId} to lead");
                        return Json(new { success = false, message = "No changes were saved to the database" });
                    }
                }
                else
                {
                    // Regular type update within Contacts table
                    contact.Type = newType;
                    _db.Contacts.Update(contact);
                    var saveResult = await _db.SaveChangesAsync();

                    if (saveResult > 0)
                    {
                        await transaction.CommitAsync();
                        _logger.LogInformation($"Successfully updated contact '{contact.Name}' (ID: {contactId}) type from '{oldType}' to '{newType}' in database");

                        return Json(new {
                            success = true,
                            message = $"Contact type updated successfully from {oldType} to {newType}",
                            shouldRedirect = false,
                            oldType = oldType,
                            newType = newType
                        });
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError($"No changes were saved when updating contact {contactId} type to '{newType}'");
                        return Json(new { success = false, message = "No changes were saved to the database" });
                    }
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating contact {contactId} type to '{newType}'");
                return Json(new { success = false, message = "An error occurred while updating the contact type" });
            }
        }

        // POST: /Contacts/UpdateLastContacted
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLastContacted(int contactId, string lastContacted)
        {
            try
            {
                _logger.LogInformation($"Updating last contacted for contact {contactId} to '{lastContacted}'");

                var contact = await _db.Contacts.FindAsync(contactId);
                if (contact == null)
                {
                    _logger.LogWarning($"Contact with ID {contactId} not found");
                    return Json(new { success = false, message = "Contact not found" });
                }

                if (!contact.IsActive)
                {
                    _logger.LogWarning($"Contact with ID {contactId} is inactive");
                    return Json(new { success = false, message = "Contact is inactive" });
                }

                // Parse the date string (can be empty for "never contacted")
                DateTime? newLastContacted = null;
                if (!string.IsNullOrEmpty(lastContacted))
                {
                    if (DateTime.TryParse(lastContacted, out DateTime parsedDate))
                    {
                        // Normalize to UTC for PostgreSQL timestamptz
                        if (parsedDate.Kind == DateTimeKind.Unspecified)
                        {
                            newLastContacted = DateTime.SpecifyKind(parsedDate, DateTimeKind.Local).ToUniversalTime();
                        }
                        else if (parsedDate.Kind == DateTimeKind.Local)
                        {
                            newLastContacted = parsedDate.ToUniversalTime();
                        }
                        else
                        {
                            newLastContacted = parsedDate;
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Invalid date format '{lastContacted}' for contact {contactId}");
                        return Json(new { success = false, message = "Invalid date format" });
                    }
                }

                // Update the LastContacted date
                contact.LastContacted = newLastContacted;

                _db.Contacts.Update(contact);
                var saveResult = await _db.SaveChangesAsync();

                if (saveResult > 0)
                {
                    var message = newLastContacted.HasValue
                        ? $"Last contacted date updated to {newLastContacted.Value:MMM dd, yyyy}"
                        : "Last contacted date cleared";

                    _logger.LogInformation($"Successfully updated LastContacted for contact '{contact.Name}' (ID: {contactId})");

                    return Json(new {
                        success = true,
                        message = message,
                        lastContacted = newLastContacted?.ToString("MMM dd, yyyy")
                    });
                }
                else
                {
                    _logger.LogError($"No changes were saved when updating LastContacted for contact {contactId}");
                    return Json(new { success = false, message = "No changes were saved to the database" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating last contacted for contact {contactId}");
                return Json(new { success = false, message = "An error occurred while updating the last contacted date" });
            }
        }

        // GET: /Contacts/VerifyDatabaseUpdate - Debug endpoint to verify database state
        [HttpGet]
        public async Task<IActionResult> VerifyDatabaseUpdate(int contactId)
        {
            try
            {
                var contact = await _db.Contacts.FindAsync(contactId);
                if (contact == null)
                {
                    return Json(new { found = false, message = "Contact not found" });
                }

                return Json(new {
                    found = true,
                    id = contact.Id,
                    name = contact.Name,
                    type = contact.Type,
                    isActive = contact.IsActive,
                    dateCreated = contact.DateCreated,
                    lastContacted = contact.LastContacted
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error verifying contact {contactId} in database");
                return Json(new { found = false, message = "Error checking database" });
            }
        }

        // POST: /Contacts/DeleteContact
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteContact(int contactId)
        {
            var contact = await _db.Contacts.FindAsync(contactId);
            if (contact == null) return NotFound(new { success = false, message = "Contact not found" });
            if (!contact.IsActive && contact.ArchivedAtUtc != null)
                return Ok(new { success = true, archived = true });

            // Authorization: Broker OR (agent matches)
            var fullName = User.Claims.FirstOrDefault(c => c.Type == "FullName")?.Value;
            var isBroker = User.IsInRole("Broker");
            if (!isBroker && !string.Equals(contact.Agent ?? "", fullName ?? "", StringComparison.OrdinalIgnoreCase))
                return Forbid();

            contact.IsActive = false;
            contact.ArchivedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { success = true, archived = true });
        }

        // POST: /Contacts/DeleteDocument
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteDocument(int contactId, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest(new { success = false, message = "Missing file name" });

            // Basic sanitization (no path traversal)
            var safeName = fileName.Replace("\\", "").Replace("/", "");
            if (safeName != fileName)
                return BadRequest(new { success = false, message = "Invalid file name" });

            var root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "contacts", contactId.ToString());
            var fullPath = Path.Combine(root, safeName);

            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { success = false, message = "File not found" });

            try
            {
                System.IO.File.Delete(fullPath);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}




