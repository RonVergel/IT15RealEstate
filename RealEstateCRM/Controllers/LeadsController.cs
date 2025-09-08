using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using Microsoft.Extensions.Logging;

namespace RealEstateCRM.Controllers
{
    public class LeadsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<LeadsController> _logger;

        public LeadsController(AppDbContext db, ILogger<LeadsController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // GET: /Leads/Index - Show leads from the Leads table
        public async Task<IActionResult> Index()
        {
            try
            {
                var leads = await _db.Leads
                    .Where(l => l.IsActive)
                    .OrderByDescending(l => l.DateCreated)
                    .ToListAsync();
                
                _logger.LogInformation($"Retrieved {leads.Count} leads from Leads table");
                return View(leads);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving leads");
                return View(new List<Lead>());
            }
        }

        // POST: /Leads/CreateLead
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLead(Lead lead)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    lead.DateCreated = DateTime.Now;
                    lead.IsActive = true;
                    lead.LeadSource = "Manual"; // Mark as manually created
                    
                    _db.Leads.Add(lead);
                    var saveResult = await _db.SaveChangesAsync();
                    
                    if (saveResult > 0)
                    {
                        _logger.LogInformation($"Lead '{lead.Name}' created successfully with ID {lead.Id} in Leads table");
                        
                        // Set success message in TempData
                        TempData["SuccessMessage"] = "Lead Added";
                        
                        return RedirectToAction(nameof(Index));
                    }
                    else
                    {
                        _logger.LogWarning($"No changes were saved when creating lead '{lead.Name}'");
                        ModelState.AddModelError("", "No changes were saved. Please try again.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error creating lead '{lead.Name}'");
                    ModelState.AddModelError("", "An error occurred while saving the lead. Please try again.");
                }
            }
            else
            {
                _logger.LogWarning("Model validation failed when creating lead");
                foreach (var modelError in ModelState.Values.SelectMany(v => v.Errors))
                {
                    _logger.LogWarning($"Validation error: {modelError.ErrorMessage}");
                }
            }

            // If we got this far, something failed, redisplay form with validation errors
            var leads = await _db.Leads
                .Where(l => l.IsActive)
                .OrderByDescending(l => l.DateCreated)
                .ToListAsync();
            
            return View("Index", leads);
        }

        // POST: /Leads/ConvertContactToLead - Convert a contact to a lead
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConvertContactToLead(int contactId)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _logger.LogInformation($"Converting contact {contactId} to lead");

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

                // Create new lead from contact data
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
                    LeadSource = "Converted"
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
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error converting contact {contactId} to lead");
                return Json(new { success = false, message = "An error occurred while converting the contact to a lead" });
            }
        }

        // POST: /Leads/UpdateLeadType - Handle lead type changes
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLeadType(int leadId, string newType)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _logger.LogInformation($"Attempting to update lead {leadId} type to '{newType}'");

                var lead = await _db.Leads.FindAsync(leadId);
                if (lead == null)
                {
                    _logger.LogWarning($"Lead with ID {leadId} not found");
                    return Json(new { success = false, message = "Lead not found" });
                }

                if (!lead.IsActive)
                {
                    _logger.LogWarning($"Lead with ID {leadId} is inactive");
                    return Json(new { success = false, message = "Lead is inactive" });
                }

                // Validate the new type
                var validTypes = new[] { "Agent", "Client" };
                if (!validTypes.Contains(newType))
                {
                    _logger.LogWarning($"Invalid contact type '{newType}' for lead {leadId}");
                    return Json(new { success = false, message = "Invalid contact type. Leads can only be converted to Agent or Client." });
                }

                // Convert lead to contact with the specified type
                var contact = new Contact
                {
                    Name = lead.Name,
                    Agent = lead.Agent,
                    Email = lead.Email,
                    Phone = lead.Phone,
                    Type = newType,
                    DateCreated = lead.DateCreated,
                    LastContacted = lead.LastContacted,
                    Notes = lead.Notes,
                    IsActive = true
                };

                // Add the contact to the Contacts table
                _db.Contacts.Add(contact);
                
                // Remove the lead from the Leads table
                _db.Leads.Remove(lead);

                var saveResult = await _db.SaveChangesAsync();
                
                if (saveResult > 0)
                {
                    await transaction.CommitAsync();
                    _logger.LogInformation($"Successfully converted lead '{lead.Name}' (ID: {leadId}) to contact with type '{newType}' (ID: {contact.Id})");
                    
                    return Json(new { 
                        success = true, 
                        message = $"Lead '{lead.Name}' has been converted to {newType}",
                        contactId = contact.Id,
                        shouldRedirect = true,
                        redirectUrl = "/Contacts/Index"
                    });
                }
                else
                {
                    await transaction.RollbackAsync();
                    _logger.LogError($"No changes were saved when converting lead {leadId} to {newType}");
                    return Json(new { success = false, message = "No changes were saved to the database" });
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error converting lead {leadId} to {newType}");
                return Json(new { success = false, message = "An error occurred while converting the lead" });
            }
        }

        // GET: /Leads/VerifyDatabaseUpdate - Debug endpoint to verify database state
        [HttpGet]
        public async Task<IActionResult> VerifyDatabaseUpdate(int leadId)
        {
            try
            {
                var lead = await _db.Leads.FindAsync(leadId);
                if (lead == null)
                {
                    return Json(new { found = false, message = "Lead not found" });
                }

                return Json(new { 
                    found = true, 
                    id = lead.Id,
                    name = lead.Name,
                    isActive = lead.IsActive,
                    dateCreated = lead.DateCreated,
                    lastContacted = lead.LastContacted,
                    leadSource = lead.LeadSource,
                    originalContactId = lead.OriginalContactId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error verifying lead {leadId} in database");
                return Json(new { found = false, message = "Error checking database" });
            }
        }
    }
}
