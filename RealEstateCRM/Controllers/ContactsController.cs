using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class ContactsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ContactsController> _logger;
        private readonly Microsoft.AspNetCore.Identity.UI.Services.IEmailSender _emailSender;

        public ContactsController(AppDbContext db, ILogger<ContactsController> logger, Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender)
        {
            _db = db;
            _logger = logger;
            _emailSender = emailSender;
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

                _logger.LogInformation($"Retrieved {contacts.Count} contacts for page {page} of {totalPages}");
                
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalCount = totalCount;

                return View(contacts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving contacts");
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
                _logger.LogError(ex, "Error retrieving contact details for {Id}", id);
                return Json(new { success = false, message = "An error occurred while retrieving the contact" });
            }
        }

        // POST: /Contacts/SendEmail
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendEmail(int contactId, string subject, string body)
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

                await _emailSender.SendEmailAsync(c.Email!, subject.Trim(), body);

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
    }
}

