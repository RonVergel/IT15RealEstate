using Microsoft.AspNetCore.Mvc;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace RealEstateCRM.Controllers
{
    public class PropertiesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<PropertiesController> _logger;

        public PropertiesController(AppDbContext context, IWebHostEnvironment webHostEnvironment, ILogger<PropertiesController> logger)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        // GET: Properties/Index
        public async Task<IActionResult> Index()
        {
            try
            {
                var properties = await _context.Properties.OrderByDescending(p => p.ListingTime).ToListAsync();
                _logger.LogInformation($"Retrieved {properties.Count} properties for display");
                return View(properties);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving properties");
                return View(new List<Property>());
            }
        }

        // POST: Properties/Index - Handle form submission
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(Property property)
        {
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
                    
                    // Return all properties for display along with validation errors
                    var allPropertiesWithErrors = await _context.Properties.OrderByDescending(p => p.ListingTime).ToListAsync();
                    return View(allPropertiesWithErrors);
                }

                // Handle image upload
                if (property.ImageFile != null && property.ImageFile.Length > 0)
                {
                    var result = await HandleImageUpload(property);
                    if (!result.Success)
                    {
                        ModelState.AddModelError("ImageFile", result.ErrorMessage);
                        var properties = await _context.Properties.OrderByDescending(p => p.ListingTime).ToListAsync();
                        return View(properties);
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
                    
                    // Redirect to avoid form resubmission on refresh
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

            // If we got this far, something failed, redisplay form with validation errors
            var allProperties = await _context.Properties.OrderByDescending(p => p.ListingTime).ToListAsync();
            return View(allProperties);
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

        // Add this method to help debug
        [HttpGet]
        public async Task<IActionResult> Debug()
        {
            var count = await _context.Properties.CountAsync();
            var properties = await _context.Properties.Take(10).ToListAsync();
            
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
    }
}
