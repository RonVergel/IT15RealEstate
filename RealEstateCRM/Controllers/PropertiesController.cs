using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class PropertiesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<PropertiesController> _logger;
        private readonly UserManager<IdentityUser> _userManager;

        public PropertiesController(AppDbContext context, IWebHostEnvironment webHostEnvironment, ILogger<PropertiesController> logger, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
            _userManager = userManager;
        }

        // GET: Properties/Index?page=1
        [HttpGet]
        public async Task<IActionResult> Index(int page = 1)
        {
            const int pageSize = 10;

            try
            {
                var totalCount = await _context.Properties.CountAsync();
                var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
                page = Math.Max(1, Math.Min(page, totalPages));

                var properties = await _context.Properties
                    .OrderByDescending(p => p.ListingTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                _logger.LogInformation($"Retrieved {properties.Count} properties for page {page} of {totalPages}");

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalCount = totalCount;

                return View(properties);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving properties");
                ViewBag.CurrentPage = 1;
                ViewBag.TotalPages = 1;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalCount = 0;
                return View(new List<Property>());
            }
        }

        // POST: Properties/Create - Handle form submission (was previously POST Index)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Property property)
        {
            const int pageSize = 10;
            try
            {
                _logger.LogInformation($"Attempting to save property: {property.Title ?? "No Title"}");

                // Set default values for required fields if they're empty
                if (string.IsNullOrWhiteSpace(property.Title))
                {
                    ModelState.AddModelError("Title", "Property Title is required.");
                }

                if (string.IsNullOrWhiteSpace(property.Address))
                {
                    ModelState.AddModelError("Address", "Address is required.");
                }

                if (string.IsNullOrWhiteSpace(property.PropertyType))
                {
                    ModelState.AddModelError("PropertyType", "Property Type is required.");
                }

                if (string.IsNullOrWhiteSpace(property.Type))
                {
                    ModelState.AddModelError("Type", "Property Type (Buying/Viewing) is required.");
                }

                // Set default values for optional fields
                property.ListingStatus ??= "Active";
                property.ListingTime = property.ListingTime == default ? DateTime.Now : property.ListingTime;

                // Automatically calculate SQFT from Area (sqm)
                property.CalculateSQFT();
                
                // Automatically calculate Price Per SQFT
                property.CalculatePricePerSQFT();

                // Custom validation for PricePerSQFT based on PropertyType
                if (property.PropertyType == "Raw Land" && (!property.PricePerSQFT.HasValue || property.PricePerSQFT <= 0))
                {
                    ModelState.AddModelError("PricePerSQFT", "Unable to calculate Price Per SQFT. Please ensure Area and Price are provided.");
                }

                // Calculate DaysOnMarket
                if (property.ListingTime != default(DateTime))
                {
                    property.DaysOnMarket = (DateTime.Now - property.ListingTime).Days;
                }

                // Log validation state
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model validation failed when creating property");
                    foreach (var modelError in ModelState)
                    {
                        foreach (var error in modelError.Value.Errors)
                        {
                            _logger.LogWarning($"Validation error in {modelError.Key}: {error.ErrorMessage}");
                        }
                    }
                    
                    // Return paged properties for display along with validation errors (page 1)
                    var totalCount = await _context.Properties.CountAsync();
                    var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
                    var page = 1;
                    var allPropertiesWithErrors = await _context.Properties.OrderByDescending(p => p.ListingTime)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToListAsync();

                    ViewBag.CurrentPage = page;
                    ViewBag.TotalPages = totalPages;
                    ViewBag.PageSize = pageSize;
                    ViewBag.TotalCount = totalCount;
                    return View("Index", allPropertiesWithErrors);
                }

                // Handle image upload
                if (property.ImageFile != null && property.ImageFile.Length > 0)
                {
                    var result = await HandleImageUpload(property);
                    if (!result.Success)
                    {
                        ModelState.AddModelError("ImageFile", result.ErrorMessage);

                        var totalCount = await _context.Properties.CountAsync();
                        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
                        var page = 1;
                        var properties = await _context.Properties.OrderByDescending(p => p.ListingTime)
                            .Skip((page - 1) * pageSize)
                            .Take(pageSize)
                            .ToListAsync();

                        ViewBag.CurrentPage = page;
                        ViewBag.TotalPages = totalPages;
                        ViewBag.PageSize = pageSize;
                        ViewBag.TotalCount = totalCount;
                        return View("Index", properties);
                    }
                }

                // Save to database
                _context.Properties.Add(property);
                var saveResult = await _context.SaveChangesAsync();
                
                if (saveResult > 0)
                {
                    _logger.LogInformation($"Property '{property.Title}' created successfully with ID {property.Id}");
                    
                    // Set success message in TempData
                    TempData["SuccessMessage"] = "Property Added Successfully";
                    
                    // Redirect to index (first page)
                    return RedirectToAction(nameof(Index));
                }
                else
                {
                    _logger.LogWarning($"No changes were saved when creating property '{property.Title}'");
                    ModelState.AddModelError("", "No changes were saved. Please try again.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating property '{property.Title ?? "Unknown"}'");
                ModelState.AddModelError("", $"An error occurred while saving the property: {ex.Message}");
            }

            // If we got this far, something failed, redisplay paged properties (page 1) with validation errors
            var totalCountFinal = await _context.Properties.CountAsync();
            var totalPagesFinal = totalCountFinal == 0 ? 1 : (int)Math.Ceiling(totalCountFinal / (double)pageSize);
            var firstPage = 1;
            var allProperties = await _context.Properties.OrderByDescending(p => p.ListingTime)
                .Skip((firstPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = firstPage;
            ViewBag.TotalPages = totalPagesFinal;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCountFinal;
            return View("Index", allProperties);
        }

        private async Task<(bool Success, string ErrorMessage)> HandleImageUpload(Property property)
        {
            try
            {
                // Validate file type
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
                var fileExtension = Path.GetExtension(property.ImageFile.FileName).ToLowerInvariant();
                
                if (!allowedExtensions.Contains(fileExtension))
                {
                    return (false, "Please upload a valid image file (JPG, PNG, GIF, BMP).");
                }

                // Validate file size (max 5MB)
                if (property.ImageFile.Length > 5 * 1024 * 1024)
                {
                    return (false, "File size cannot exceed 5MB.");
                }

                // Create uploads directory if it doesn't exist
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Generate unique filename
                var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(property.ImageFile.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Save the file
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await property.ImageFile.CopyToAsync(fileStream);
                }

                // Store the relative path in the database
                property.ImagePath = "/uploads/" + uniqueFileName;
                
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling image upload");
                return (false, "Error uploading image. Please try again.");
            }
        }

        // GET: Properties/GetProperty - Get property data for editing
        [HttpGet]
        public async Task<IActionResult> GetProperty(int id)
        {
            try
            {
                var property = await _context.Properties.FindAsync(id);
                if (property == null)
                {
                    return Json(new { success = false, message = "Property not found" });
                }

                return Json(new { 
                    success = true, 
                    property = new {
                        id = property.Id,
                        title = property.Title,
                        address = property.Address,
                        price = property.Price,
                        area = property.Area,
                        bedrooms = property.Bedrooms,
                        bathrooms = property.Bathrooms,
                        propertyType = property.PropertyType,
                        type = property.Type,
                        listingStatus = property.ListingStatus,
                        listingTime = property.ListingTime.ToString("yyyy-MM-ddTHH:mm"),
                        propertyLink = property.PropertyLink,
                        agent = property.Agent,
                        description = property.Description,
                        imagePath = property.ImagePath
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving property {id}");
                return Json(new { success = false, message = "An error occurred while retrieving the property" });
            }
        }

        // POST: Properties/UpdateProperty - Handle property updates
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProperty(Property property)
        {
            try
            {
                var existingProperty = await _context.Properties.FindAsync(property.Id);
                if (existingProperty == null)
                {
                    return Json(new { success = false, message = "Property not found" });
                }

                // Update properties with null-safe assignments
                existingProperty.Title = !string.IsNullOrWhiteSpace(property.Title) ? property.Title : existingProperty.Title;
                existingProperty.Address = !string.IsNullOrWhiteSpace(property.Address) ? property.Address : existingProperty.Address;
                existingProperty.Price = property.Price;
                existingProperty.Area = property.Area ?? existingProperty.Area;
                existingProperty.Bedrooms = property.Bedrooms ?? existingProperty.Bedrooms;
                existingProperty.Bathrooms = property.Bathrooms ?? existingProperty.Bathrooms;
                existingProperty.PropertyType = !string.IsNullOrWhiteSpace(property.PropertyType) ? property.PropertyType : existingProperty.PropertyType;
                existingProperty.Type = !string.IsNullOrWhiteSpace(property.Type) ? property.Type : existingProperty.Type;
                existingProperty.ListingStatus = !string.IsNullOrWhiteSpace(property.ListingStatus) ? property.ListingStatus : existingProperty.ListingStatus;
                existingProperty.ListingTime = property.ListingTime != default(DateTime) ? property.ListingTime : existingProperty.ListingTime;
                existingProperty.PropertyLink = property.PropertyLink ?? existingProperty.PropertyLink;
                
                // FIX: Allow agent to be updated to empty string (was causing the issue)
                existingProperty.Agent = property.Agent; // This allows setting agent to empty string
                
                existingProperty.Description = property.Description ?? existingProperty.Description;

                // Recalculate SQFT and Price Per SQFT
                existingProperty.CalculateSQFT();
                existingProperty.CalculatePricePerSQFT();

                // Calculate DaysOnMarket
                if (existingProperty.ListingTime != default(DateTime))
                {
                    existingProperty.DaysOnMarket = (DateTime.Now - existingProperty.ListingTime).Days;
                }

                // Handle image upload if provided
                if (property.ImageFile != null && property.ImageFile.Length > 0)
                {
                    var result = await HandleImageUpload(property);
                    if (!result.Success)
                    {
                        return Json(new { success = false, message = result.ErrorMessage });
                    }

                    // Delete old image if it exists
                    if (!string.IsNullOrEmpty(existingProperty.ImagePath))
                    {
                        var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, existingProperty.ImagePath.TrimStart('/'));
                        if (System.IO.File.Exists(oldImagePath))
                        {
                            System.IO.File.Delete(oldImagePath);
                        }
                    }

                    existingProperty.ImagePath = property.ImagePath;
                }

                _context.Properties.Update(existingProperty);
                var saveResult = await _context.SaveChangesAsync();
                
                if (saveResult > 0)
                {
                    _logger.LogInformation($"Property '{existingProperty.Title}' updated successfully with ID {existingProperty.Id}");
                    
                    // Set success message in TempData
                    TempData["SuccessMessage"] = "Property Updated Successfully";
                    
                    return Json(new { success = true, message = "Property updated successfully" });
                }
                else
                {
                    _logger.LogWarning($"No changes were saved when updating property '{existingProperty.Title}'");
                    return Json(new { success = false, message = "No changes were saved. Please try again." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating property {property.Id}");
                return Json(new { success = false, message = "An error occurred while updating the property. Please try again." });
            }
        }

        // JSON-friendly update endpoint used by client-side preview/save
        // POST /Properties/Update
        [HttpPost]
        [Route("Properties/Update")]
        public async Task<IActionResult> Update([FromBody] UpdatePropertyDto dto)
        {
            if (dto == null)
            {
                return Json(new { success = false, message = "Invalid payload" });
            }

            try
            {
                var existingProperty = await _context.Properties.FindAsync(dto.Id);
                if (existingProperty == null)
                {
                    return Json(new { success = false, message = "Property not found" });
                }

                // Update fields - use null to mean "leave unchanged"; allow empty string to clear where appropriate
                if (dto.Title != null) existingProperty.Title = dto.Title;
                if (dto.Address != null) existingProperty.Address = dto.Address;
                if (dto.Price.HasValue) existingProperty.Price = dto.Price.Value;
                if (dto.Area.HasValue) existingProperty.Area = (int?)dto.Area;
                if (dto.Bedrooms.HasValue) existingProperty.Bedrooms = dto.Bedrooms;
                if (dto.Bathrooms.HasValue) existingProperty.Bathrooms = dto.Bathrooms;
                if (dto.PropertyType != null) existingProperty.PropertyType = dto.PropertyType;
                if (dto.Type != null) existingProperty.Type = dto.Type;
                if (dto.ListingStatus != null) existingProperty.ListingStatus = dto.ListingStatus;
                if (dto.ListingTime.HasValue) existingProperty.ListingTime = dto.ListingTime.Value;
                if (dto.PropertyLink != null) existingProperty.PropertyLink = dto.PropertyLink;

                // Allow agent to be set to empty string explicitly; if dto.Agent is null, keep existing
                if (dto.Agent != null) existingProperty.Agent = dto.Agent;

                if (dto.Description != null) existingProperty.Description = dto.Description;

                // Recalculate derived values
                existingProperty.CalculateSQFT();
                existingProperty.CalculatePricePerSQFT();

                if (existingProperty.ListingTime != default(DateTime))
                {
                    existingProperty.DaysOnMarket = (DateTime.Now - existingProperty.ListingTime).Days;
                }

                _context.Properties.Update(existingProperty);
                var saveResult = await _context.SaveChangesAsync();

                if (saveResult > 0)
                {
                    _logger.LogInformation($"Property '{existingProperty.Title}' updated via JSON API (ID {existingProperty.Id})");
                    return Json(new { success = true, message = "Property updated successfully" });
                }

                return Json(new { success = false, message = "No changes were saved. Please try again." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating property (JSON) id={dto.Id}");
                return Json(new { success = false, message = "An error occurred while updating the property. Please try again." });
            }
        }

        // GET: Properties/GetAgents?q=searchTerm
        // Returns active contacts with Type == "Agent" AND Identity users in role "Agent" (merged, de-duplicated)
        [HttpGet]
        public async Task<IActionResult> GetAgents(string q = "")
        {
            try
            {
                // DTO for response
                List<AgentDto> results = new();

                // 1) Contacts table agents
                var contactQuery = _context.Contacts
                                    .AsQueryable()
                                    .Where(c => c.IsActive && c.Type == "Agent");

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var pattern = $"%{q}%";
                    contactQuery = contactQuery.Where(c => EF.Functions.Like(c.Name, pattern));
                }

                var contactAgents = await contactQuery
                    .OrderBy(c => c.Name)
                    .Select(c => new AgentDto { Id = $"c:{c.Id}", Name = c.Name })
                    .ToListAsync();

                results.AddRange(contactAgents);

                // 2) Identity users in role "Agent"
                try
                {
                    if (_userManager != null)
                    {
                        var usersInRole = await _userManager.GetUsersInRoleAsync("Agent");
                        IEnumerable<IdentityUser> filteredUsers = usersInRole;

                        if (!string.IsNullOrWhiteSpace(q))
                        {
                            var qLower = q.ToLowerInvariant();
                            filteredUsers = usersInRole.Where(u =>
                                (!string.IsNullOrEmpty(u.UserName) && u.UserName.ToLowerInvariant().Contains(qLower)) ||
                                (!string.IsNullOrEmpty(u.Email) && u.Email.ToLowerInvariant().Contains(qLower))
                            );
                        }

                        var userAgents = filteredUsers
                            .Select(u => new AgentDto { Id = $"u:{u.Id}", Name = string.IsNullOrEmpty(u.UserName) ? u.Email ?? u.Id : u.UserName })
                            .ToList();

                        results.AddRange(userAgents);
                    }
                }
                catch (Exception ex)
                {
                    // don't fail the whole request if UserManager role lookup fails; log and continue with contacts only
                    _logger.LogWarning(ex, "Failed to fetch Identity users in role 'Agent'. Returning contact-based agents only.");
                }

                // De-duplicate by Name (case-insensitive) and order
                var merged = results
                    .GroupBy(a => a.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(a => a.Name)
                    .Take(100)
                    .ToList();

                return Json(merged);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving agents");
                return Json(new { success = false, message = "Error retrieving agents" });
            }
        }

        // Add this method to help debug
        [HttpGet]
        public async Task<IActionResult> Debug()
        {
            var count = await _context.Properties.CountAsync();
            var properties = await _context.Properties.ToListAsync();
            
            return Json(new { 
                totalCount = count, 
                properties = properties.Select(p => new { 
                    p.Id, 
                    p.Title, 
                    p.Address, 
                    p.Price,
                    p.PropertyType,
                    p.Type,
                    p.ListingTime
                }) 
            });
        }

        // Add this method inside the PropertiesController class
        [HttpGet]
        public async Task<IActionResult> GetPreferredPropertyTypesByOccupation(string occupation)
        {
            if (string.IsNullOrWhiteSpace(occupation))
                return BadRequest(new { success = false, message = "Occupation is required" });

            try
            {
                // Find contact and lead names with that occupation
                var contactNames = await _context.Contacts
                    .Where(c => c.Occupation == occupation)
                    .Select(c => c.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToListAsync();

                var leadNames = await _context.Leads
                    .Where(l => l.Occupation == occupation)
                    .Select(l => l.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToListAsync();

                var clientNames = contactNames.Concat(leadNames)
                                             .Where(n => !string.IsNullOrEmpty(n))
                                             .Distinct(StringComparer.OrdinalIgnoreCase)
                                             .ToList();

                // Aggregate from Deals (preferred: deals link a client to a property)
                var dealAgg = new List<(string Type, int Count)>();
                if (clientNames.Any())
                {
                    // Project to anonymous type in the expression tree (no tuple literal)
                    var dealAnon = await _context.Deals
                        .Where(d => !string.IsNullOrEmpty(d.ClientName) && clientNames.Contains(d.ClientName))
                        .Join(_context.Properties,
                              d => d.PropertyId,
                              p => p.Id,
                              (d, p) => p.PropertyType)
                        .GroupBy(pt => pt ?? "Unknown")
                        .Select(g => new { Type = g.Key, Count = g.Count() })
                        .ToListAsync();

                    dealAgg = dealAnon.Select(a => (a.Type, a.Count)).ToList();
                }

                // Fallback: aggregate properties where Agent matches contact/lead name
                var propAgg = new List<(string Type, int Count)>();
                if (clientNames.Any())
                {
                    var propAnon = await _context.Properties
                        .Where(p => !string.IsNullOrEmpty(p.Agent) && clientNames.Contains(p.Agent))
                        .GroupBy(p => p.PropertyType ?? "Unknown")
                        .Select(g => new { Type = g.Key, Count = g.Count() })
                        .ToListAsync();

                    propAgg = propAnon.Select(a => (a.Type, a.Count)).ToList();
                }

                // Merge results (deal counts prioritized, then add property counts)
                var merged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var tuple in dealAgg)
                    merged[tuple.Type] = merged.GetValueOrDefault(tuple.Type) + tuple.Count;
                foreach (var tuple in propAgg)
                    merged[tuple.Type] = merged.GetValueOrDefault(tuple.Type) + tuple.Count;

                // If no matches, return guidance
                if (!merged.Any())
                {
                    return Json(new
                    {
                        success = true,
                        occupation,
                        results = Array.Empty<object>(),
                        message = "No matching deals or properties found for the given occupation. Consider linking clients to deals with a ContactId for accurate analytics."
                    });
                }

                // Return ordered results
                var results = merged
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new { PropertyType = kv.Key, Count = kv.Value })
                    .ToList();

                return Json(new { success = true, occupation, results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error computing preferred property types for occupation {Occupation}", occupation);
                return Json(new { success = false, message = "An error occurred" });
            }
        }
    }

    // DTO used by the JSON update endpoint
    public class UpdatePropertyDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Address { get; set; }
        public decimal? Price { get; set; }
        public double? Area { get; set; }
        public int? Bedrooms { get; set; }
        public int? Bathrooms { get; set; }
        public string? PropertyType { get; set; }
        public string? Type { get; set; }
        public string? ListingStatus { get; set; }
        public DateTime? ListingTime { get; set; }
        public string? PropertyLink { get; set; }
        public string? Agent { get; set; }
        public string? Description { get; set; }
    }

    // Small DTO used for GetAgents response
    public class AgentDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}