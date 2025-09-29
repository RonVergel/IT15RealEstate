using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;

namespace RealEstateCRM.Controllers
{
  [Authorize]
  public class PropertiesController : Controller
  {
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILogger<PropertiesController> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RealEstateCRM.Services.Notifications.INotificationService _notifications;
    private readonly IConfiguration _configuration;

    public PropertiesController(
        AppDbContext context,
        IWebHostEnvironment webHostEnvironment,
        ILogger<PropertiesController> logger,
        UserManager<ApplicationUser> userManager,
        RealEstateCRM.Services.Notifications.INotificationService notifications,
        IConfiguration configuration)
    {
      _context = context;
      _webHostEnvironment = webHostEnvironment;
      _logger = logger;
      _userManager = userManager;
      _notifications = notifications;
      _configuration = configuration;
    }

    // GET: Properties/Index with optional filters
    // Example: /Properties?page=1&q=condo&propertyType=Residential&listingStatus=Active&minPrice=100000&maxPrice=500000&minBeds=2&minBaths=1&buyingType=Sell&assigned=unassigned
    [HttpGet]
    public async Task<IActionResult> Index(
        int page = 1,
        string? q = null,
        string? propertyType = null,
        string? listingStatus = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int? minBeds = null,
        int? minBaths = null,
        string? buyingType = null,
        string? assigned = null)
    {
      const int pageSize = 10;

      try
      {
        // Base query
        var query = _context.Properties.AsQueryable();

        // Hide properties that already have a Closed or Archived deal
        // (treated as sold/removed and should not appear in the properties catalog)
        query = query.Where(p => !_context.Deals.Any(d => d.PropertyId == p.Id && (d.Status == "Closed" || d.Status == "Archived")));

        // Apply filters
        if (!string.IsNullOrWhiteSpace(q))
        {
          var kw = q.Trim();
          // Case-insensitive search across common text fields
          query = query.Where(p =>
              (p.Title != null && EF.Functions.ILike(p.Title, "%" + kw + "%")) ||
              (p.Address != null && EF.Functions.ILike(p.Address, "%" + kw + "%")) ||
              (p.Description != null && EF.Functions.ILike(p.Description, "%" + kw + "%")) ||
              (p.Agent != null && EF.Functions.ILike(p.Agent, "%" + kw + "%"))
          );
        }
        if (!string.IsNullOrWhiteSpace(propertyType))
          query = query.Where(p => p.PropertyType == propertyType);
        if (!string.IsNullOrWhiteSpace(listingStatus))
          query = query.Where(p => p.ListingStatus == listingStatus);
        if (!string.IsNullOrWhiteSpace(buyingType))
          query = query.Where(p => p.Type == buyingType);
        if (minPrice.HasValue)
          query = query.Where(p => p.Price >= minPrice.Value);
        if (maxPrice.HasValue)
          query = query.Where(p => p.Price <= maxPrice.Value);
        if (minBeds.HasValue)
          query = query.Where(p => (p.Bedrooms ?? 0) >= minBeds.Value);
        if (minBaths.HasValue)
          query = query.Where(p => (p.Bathrooms ?? 0) >= minBaths.Value);
        if (!string.IsNullOrWhiteSpace(assigned))
        {
          if (string.Equals(assigned, "assigned", StringComparison.OrdinalIgnoreCase))
            query = query.Where(p => !string.IsNullOrEmpty(p.Agent));
          else if (string.Equals(assigned, "unassigned", StringComparison.OrdinalIgnoreCase))
            query = query.Where(p => string.IsNullOrEmpty(p.Agent));
        }

        var totalCount = await query.CountAsync();
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Max(1, Math.Min(page, totalPages));

        var properties = await query
            .OrderByDescending(p => p.ListingTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        _logger.LogInformation($"Retrieved {properties.Count} properties for page {page} of {totalPages}");

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;

        // Persist current filters in ViewBag for the view + pagination
        ViewBag.Q = q;
        ViewBag.FilterPropertyType = propertyType;
        ViewBag.FilterListingStatus = listingStatus;
        ViewBag.FilterMinPrice = minPrice;
        ViewBag.FilterMaxPrice = maxPrice;
        ViewBag.FilterMinBeds = minBeds;
        ViewBag.FilterMinBaths = minBaths;
        ViewBag.FilterBuyingType = buyingType;
        ViewBag.FilterAssigned = assigned;

        // For dropdown options, gather distinct values from DB
        ViewBag.PropertyTypes = await _context.Properties
            .Select(p => p.PropertyType)
            .Where(s => s != null && s != "")
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
        ViewBag.ListingStatuses = await _context.Properties
            .Select(p => p.ListingStatus)
            .Where(s => s != null && s != "")
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
        ViewBag.BuyingTypes = await _context.Properties
            .Select(p => p.Type)
            .Where(s => s != null && s != "")
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();

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
        // If an Agent is assigned upon creation, mark as Pending; else Active
        property.ListingStatus = !string.IsNullOrWhiteSpace(property.Agent) ? "Pending" : (property.ListingStatus ?? "Active");
        property.ListingTime = property.ListingTime == default ? DateTime.UtcNow : NormalizeToUtc(property.ListingTime);

        // Calculate SQFT from Area (sqm) only if Floor Area (SQFT) not provided
        if (!property.SQFT.HasValue || property.SQFT <= 0)
        {
          property.CalculateSQFT();
        }

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
          property.DaysOnMarket = (DateTime.UtcNow - property.ListingTime).Days;
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

          // Set TempData to show the add modal with errors
          TempData["ShowAddModal"] = true;
          TempData["ErrorMessage"] = "Please correct the errors below and try again.";
          TempData["PropertyModel"] = new
          {
            title = property.Title,
            address = property.Address,
            price = property.Price,
            area = property.Area,
            sqft = property.SQFT,
            bedrooms = property.Bedrooms,
            bathrooms = property.Bathrooms,
            propertyType = property.PropertyType,
            type = property.Type,
            listingStatus = property.ListingStatus,
            listingTime = property.ListingTime,
            propertyLink = property.PropertyLink,
            agent = property.Agent,
            description = property.Description
          };

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

        // Handle image upload (now uploads to Supabase public bucket)
        if (property.ImageFile != null && property.ImageFile.Length > 0)
        {
          var result = await HandleImageUpload(property);
          if (!result.Success)
          {
            ModelState.AddModelError("ImageFile", result.ErrorMessage);

            // Set TempData to show the add modal with errors
            TempData["ShowAddModal"] = true;
            TempData["ErrorMessage"] = result.ErrorMessage;
            TempData["PropertyModel"] = new
            {
              title = property.Title,
              address = property.Address,
              price = property.Price,
              area = property.Area,
              sqft = property.SQFT,
              bedrooms = property.Bedrooms,
              bathrooms = property.Bathrooms,
              propertyType = property.PropertyType,
              type = property.Type,
              listingStatus = property.ListingStatus,
              listingTime = property.ListingTime,
              propertyLink = property.PropertyLink,
              agent = property.Agent,
              description = property.Description
            };

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
          // Notify: property created
          try
          {
            var actor = await _userManager.GetUserAsync(User);
            string actorName = actor?.UserName ?? actor?.Email ?? "Someone";
            try
            {
              var claims = actor != null ? await _userManager.GetClaimsAsync(actor) : null;
              var fullName = claims?.FirstOrDefault(c => c.Type == "FullName")?.Value;
              if (!string.IsNullOrWhiteSpace(fullName)) actorName = fullName;
            }
            catch { }

            var msg = $"{actorName} added a property: '{property.Title}'.";
            await _notifications.NotifyRoleAsync("Broker", msg, "/Properties", actor?.Id, "property:create");
            await _notifications.NotifyRoleAsync("Agent", msg, "/Properties", actor?.Id, "property:create");

            // If an agent was assigned on create, notify that specific agent
            if (!string.IsNullOrWhiteSpace(property.Agent))
            {
              try
              {
                var allUsers = _userManager.Users.ToList();
                foreach (var u in allUsers)
                {
                  var name = u.UserName ?? u.Email ?? string.Empty;
                  try
                  {
                    var c = await _userManager.GetClaimsAsync(u);
                    var full = c.FirstOrDefault(x => x.Type == "FullName")?.Value;
                    if (!string.IsNullOrWhiteSpace(full)) name = full;
                  }
                  catch { }

                  if (string.Equals(name, property.Agent, StringComparison.OrdinalIgnoreCase))
                  {
                    var agentMsg = $"{actorName} assigned you a property to review: '{property.Title}'.";
                    await _notifications.NotifyUserAsync(u.Id, agentMsg, "/PendingAssignments", actor?.Id, "assignment:new");
                    break;
                  }
                }
              }
              catch { }
            }
          }
          catch { }

          // Set success message in TempData
          TempData["SuccessMessage"] = "Property Added Successfully";

          // If an agent is assigned, decide redirect based on current user's role and identity
          if (!string.IsNullOrWhiteSpace(property.Agent))
          {
            try
            {
              var currentUser = await _userManager.GetUserAsync(User);
              var isAgent = currentUser != null && await _userManager.IsInRoleAsync(currentUser, "Agent");
              var isBroker = currentUser != null && await _userManager.IsInRoleAsync(currentUser, "Broker");

              string? displayName = currentUser?.UserName ?? currentUser?.Email;
              try
              {
                var claims = currentUser != null ? await _userManager.GetClaimsAsync(currentUser) : null;
                var fullName = claims?.FirstOrDefault(c => c.Type == "FullName")?.Value;
                if (!string.IsNullOrWhiteSpace(fullName)) displayName = fullName;
              }
              catch { }

              // Only send to Pending Assignments if the current user is the same assigned Agent (agent-only view)
              if (isAgent && !isBroker && !string.IsNullOrWhiteSpace(displayName) &&
                  property.Agent.Equals(displayName, StringComparison.OrdinalIgnoreCase))
              {
                return RedirectToAction("Index", "PendingAssignments");
              }
            }
            catch { }
          }

          // Default: go back to Properties list
          return RedirectToAction(nameof(Index));
        }
        else
        {
          _logger.LogWarning($"No changes were saved when creating property '{property.Title}'");
          ModelState.AddModelError("", "No changes were saved. Please try again.");
          TempData["ShowAddModal"] = true;
          TempData["ErrorMessage"] = "No changes were saved. Please try again.";
          TempData["PropertyModel"] = new
          {
            title = property.Title,
            address = property.Address,
            price = property.Price,
            area = property.Area,
            sqft = property.SQFT,
            bedrooms = property.Bedrooms,
            bathrooms = property.Bathrooms,
            propertyType = property.PropertyType,
            type = property.Type,
            listingStatus = property.ListingStatus,
            listingTime = property.ListingTime,
            propertyLink = property.PropertyLink,
            agent = property.Agent,
            description = property.Description
          };
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"Error creating property '{property.Title ?? "Unknown"}'");
        ModelState.AddModelError("", $"An error occurred while saving the property: {ex.Message}");
        TempData["ShowAddModal"] = true;
        TempData["ErrorMessage"] = $"An error occurred while saving the property: {ex.Message}";
        TempData["PropertyModel"] = new
        {
          title = property.Title,
          address = property.Address,
          price = property.Price,
          area = property.Area,
          sqft = property.SQFT,
          bedrooms = property.Bedrooms,
          bathrooms = property.Bathrooms,
          propertyType = property.PropertyType,
          type = property.Type,
          listingStatus = property.ListingStatus,
          listingTime = property.ListingTime,
          propertyLink = property.PropertyLink,
          agent = property.Agent,
          description = property.Description
        };
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

    // NEW: Upload to Supabase public bucket and return public URL
    private async Task<(bool Success, string ImageUrl, string ErrorMessage)> UploadToSupabaseAsync(IFormFile file)
    {
      try
      {
        if (file == null || file.Length == 0) return (false, string.Empty, "No file provided");

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
          return (false, string.Empty, "Invalid file type.");

        if (file.Length > 5 * 1024 * 1024)
          return (false, string.Empty, "File size cannot exceed 5MB.");

        var supabaseUrl = _configuration["Supabase:Url"]?.TrimEnd('/');
        var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];
        var bucket = _configuration["Supabase:Bucket"];
        var bucketIsPublic = bool.TryParse(_configuration["Supabase:BucketIsPublic"], out var pb) && pb;

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(serviceRoleKey) || string.IsNullOrEmpty(bucket))
          return (false, string.Empty, "Supabase configuration missing");

        var objectPath = $"properties/{Guid.NewGuid():N}{ext}";
        // Use multipart/form-data POST with object path in URL
        var uploadUrl = $"{supabaseUrl}/storage/v1/object/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectPath)}";

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceRoleKey);

        using var content = new MultipartFormDataContent();
        var stream = file.OpenReadStream();
        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
        // "file" is the expected form field name
        content.Add(streamContent, "file", Path.GetFileName(objectPath));

        var resp = await http.PostAsync(uploadUrl, content);
        var respBody = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
          _logger.LogWarning("Supabase upload failed {Status}: {Body}", resp.StatusCode, respBody);
          return (false, string.Empty, $"Supabase upload failed: {resp.StatusCode} - {respBody}");
        }

        // Public bucket -> public URL pattern
        if (bucketIsPublic)
        {
          var publicUrl = $"{supabaseUrl}/storage/v1/object/public/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectPath)}";
          return (true, publicUrl, string.Empty);
        }

        // Fallback: signed URL (not used for public bucket)
        var signEndpoint = $"{supabaseUrl}/storage/v1/object/sign/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectPath)}?expiresIn=3600";
        var signResp = await http.GetAsync(signEndpoint);
        if (!signResp.IsSuccessStatusCode)
        {
          return (true, $"{supabaseUrl}/storage/v1/object/public/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectPath)}", string.Empty);
        }
        var json = await signResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var signed = doc.RootElement.GetProperty("signedURL").GetString() ?? string.Empty;
        return (true, signed, string.Empty);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error uploading to Supabase");
        return (false, string.Empty, "Error uploading image");
      }
    }

    // NEW: Delete object from Supabase bucket (best-effort)
    private async Task<bool> DeleteFromSupabaseAsync(string imagePathOrUrl)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(imagePathOrUrl)) return true;

        var supabaseUrl = _configuration["Supabase:Url"]?.TrimEnd('/');
        var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];
        var bucket = _configuration["Supabase:Bucket"];
        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(serviceRoleKey) || string.IsNullOrEmpty(bucket))
          return false;

        var objectPath = ExtractObjectPathFromUrl(imagePathOrUrl, supabaseUrl, bucket);
        if (string.IsNullOrEmpty(objectPath)) return true; // not a supabase object

        var deleteUrl = $"{supabaseUrl}/storage/v1/object/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectPath)}";
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceRoleKey);
        var req = new HttpRequestMessage(HttpMethod.Delete, deleteUrl);
        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
          _logger.LogWarning("Supabase delete returned {Status} for {Object}", resp.StatusCode, objectPath);
          return false;
        }
        return true;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error deleting supabase object");
        return false;
      }
    }

    private static string ExtractObjectPathFromUrl(string url, string supabaseUrl, string bucket)
    {
      try
      {
        var marker = $"/storage/v1/object/public/{bucket}/";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return url.Substring(idx + marker.Length);

        marker = $"/storage/v1/object/{bucket}/";
        idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return url.Substring(idx + marker.Length);

        // Already an object path (no host) - return as-is
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && url.Contains("/"))
          return url;

        return string.Empty;
      }
      catch
      {
        return string.Empty;
      }
    }

    private async Task<(bool Success, string ErrorMessage)> HandleImageUpload(Property property)
    {
      try
      {
        if (property.ImageFile == null || property.ImageFile.Length == 0)
          return (true, string.Empty);

        var upload = await UploadToSupabaseAsync(property.ImageFile);
        if (!upload.Success)
        {
          return (false, upload.ErrorMessage);
        }

        property.ImagePath = upload.ImageUrl;
        return (true, string.Empty);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error handling image upload (Supabase)");
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

        return Json(new
        {
          success = true,
          property = new
          {
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

        // Track original agent for assignment-change detection
        var originalAgent = existingProperty.Agent;

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

        // Allow agent to be updated (including empty string). If assigning to a non-empty and different agent, mark as Pending.
        existingProperty.Agent = property.Agent; // This allows setting agent to empty string
        var agentAssignedOrChanged = !string.IsNullOrWhiteSpace(existingProperty.Agent) &&
            !string.Equals(originalAgent ?? string.Empty, existingProperty.Agent, StringComparison.OrdinalIgnoreCase);
        if (agentAssignedOrChanged)
        {
          existingProperty.ListingStatus = "Pending";
        }

        existingProperty.Description = property.Description ?? existingProperty.Description;

        // Recalculate SQFT and Price Per SQFT
        existingProperty.CalculateSQFT();
        existingProperty.CalculatePricePerSQFT();

        // Calculate DaysOnMarket
        if (existingProperty.ListingTime != default(DateTime))
        {
          existingProperty.DaysOnMarket = (DateTime.UtcNow - existingProperty.ListingTime).Days;
        }

        // Handle image upload if provided
        if (property.ImageFile != null && property.ImageFile.Length > 0)
        {
          var result = await HandleImageUpload(property);
          if (!result.Success)
          {
            return Json(new { success = false, message = result.ErrorMessage });
          }

          // Delete old image if it exists (attempt supabase delete)
          if (!string.IsNullOrEmpty(existingProperty.ImagePath))
          {
            try { await DeleteFromSupabaseAsync(existingProperty.ImagePath); } catch { }
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

          // If a broker assigned or changed an agent, notify that agent
          try
          {
            var currentUser = await _userManager.GetUserAsync(User);
            var isBroker = currentUser != null && await _userManager.IsInRoleAsync(currentUser, "Broker");
            if (agentAssignedOrChanged && isBroker && !string.IsNullOrWhiteSpace(existingProperty.Agent))
            {
              string actorName = currentUser?.UserName ?? currentUser?.Email ?? "Broker";
              try
              {
                var claims = currentUser != null ? await _userManager.GetClaimsAsync(currentUser) : null;
                var fullName = claims?.FirstOrDefault(c => c.Type == "FullName")?.Value;
                if (!string.IsNullOrWhiteSpace(fullName)) actorName = fullName;
              }
              catch { }

              var agentMsg = $"{actorName} assigned you a property to review: '{existingProperty.Title}'.";
              try
              {
                var allUsers = _userManager.Users.ToList();
                foreach (var u in allUsers)
                {
                  var name = u.UserName ?? u.Email ?? string.Empty;
                  try
                  {
                    var c = await _userManager.GetClaimsAsync(u);
                    var full = c.FirstOrDefault(x => x.Type == "FullName")?.Value;
                    if (!string.IsNullOrWhiteSpace(full)) name = full;
                  }
                  catch { }

                  if (string.Equals(name, existingProperty.Agent, StringComparison.OrdinalIgnoreCase))
                  {
                    await _notifications.NotifyUserAsync(u.Id, agentMsg, "/PendingAssignments", currentUser?.Id, "assignment:new");
                    break;
                  }
                }
              }
              catch { }
            }
          }
          catch { }

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
        if (dto.ListingTime.HasValue) existingProperty.ListingTime = NormalizeToUtc(dto.ListingTime.Value);
        if (dto.PropertyLink != null) existingProperty.PropertyLink = dto.PropertyLink;

        // Allow agent to be set to empty string explicitly; if dto.Agent is null, keep existing
        if (dto.Agent != null) existingProperty.Agent = dto.Agent;

        if (dto.Description != null) existingProperty.Description = dto.Description;

        // Recalculate derived values
        existingProperty.CalculateSQFT();
        existingProperty.CalculatePricePerSQFT();

        if (existingProperty.ListingTime != default(DateTime))
        {
          existingProperty.DaysOnMarket = (DateTime.UtcNow - existingProperty.ListingTime).Days;
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

    // POST: Properties/Delete
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
      try
      {
        var prop = await _context.Properties.FindAsync(id);
        if (prop == null)
          return NotFound();

        // Prevent deletion if property is referenced by any deal
        var inUse = await _context.Deals.AnyAsync(d => d.PropertyId == id);
        if (inUse)
          return BadRequest("Cannot delete a property that is referenced by existing deals.");

        // Delete image from Supabase bucket (if present)
        if (!string.IsNullOrWhiteSpace(prop.ImagePath))
        {
          try { await DeleteFromSupabaseAsync(prop.ImagePath); } catch { }
        }

        _context.Properties.Remove(prop);
        await _context.SaveChangesAsync();
        return Ok();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to delete property {Id}", id);
        return StatusCode(500, "Failed to delete property.");
      }
    }

    // POST: Properties/AcceptAssignment
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptAssignment(int id)
    {
      var prop = await _context.Properties.FindAsync(id);
      if (prop == null) return NotFound();

      var user = await _userManager.GetUserAsync(User);
      string? displayName = user?.UserName ?? user?.Email;
      try
      {
        var claims = user != null ? await _userManager.GetClaimsAsync(user) : null;
        var fullName = claims?.FirstOrDefault(c => c.Type == "FullName")?.Value;
        if (!string.IsNullOrWhiteSpace(fullName)) displayName = fullName;
      }
      catch { }

      if (!string.IsNullOrWhiteSpace(prop.Agent) && !string.IsNullOrWhiteSpace(displayName) && !prop.Agent.Equals(displayName, StringComparison.OrdinalIgnoreCase))
      {
        return Forbid();
      }

      // Enforce max active assignments per agent
      try
      {
        var settings = await _context.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        var maxActive = settings?.MaxActiveAssignmentsPerAgent ?? 5;
        if (!string.IsNullOrWhiteSpace(displayName) && maxActive > 0)
        {
          var activeCount = await _context.Deals.CountAsync(d => d.AgentName != null && d.AgentName.ToLower() == displayName!.ToLower() && d.Status != "Closed" && d.Status != "Archived");
          if (activeCount >= maxActive)
          {
            TempData["ErrorMessage"] = $"You have reached the maximum of {maxActive} active assignments. Finish some deals before accepting new ones.";
            return RedirectToAction("Index", "PendingAssignments");
          }
        }
      }
      catch { }

      // 1) Accept assignment: mark property Active
      prop.ListingStatus = "Active"; // accepted -> move out of Pending
      _context.Properties.Update(prop);

      // 2) Ensure a Deal exists in "New" status for this property
      var existingDeal = await _context.Deals.FirstOrDefaultAsync(d => d.PropertyId == prop.Id);
      if (existingDeal == null)
      {
        var maxDisplayOrder = await _context.Deals
            .Where(d => d.Status == "New")
            .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

        var newDeal = new Deal
        {
          PropertyId = prop.Id,
          Title = string.IsNullOrWhiteSpace(prop.Title) ? $"Property #{prop.Id}" : prop.Title,
          Description = prop.Description,
          AgentName = displayName,
          Status = "New",
          DisplayOrder = maxDisplayOrder + 1,
          CreatedDate = DateTime.UtcNow,
          LastUpdated = DateTime.UtcNow
        };
        _context.Deals.Add(newDeal);
      }
      else
      {
        // Move existing deal to New and assign the agent
        var maxDisplayOrder = await _context.Deals
            .Where(d => d.Status == "New")
            .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;
        existingDeal.Status = "New";
        existingDeal.AgentName = displayName;
        existingDeal.DisplayOrder = maxDisplayOrder + 1;
        existingDeal.LastUpdated = DateTime.UtcNow;
        _context.Deals.Update(existingDeal);
      }

      await _context.SaveChangesAsync();

      // Notify brokers that the agent accepted the assignment
      try
      {
        var actor = await _userManager.GetUserAsync(User);
        string actorName = displayName ?? actor?.UserName ?? actor?.Email ?? "Agent";
        var msg = $"{actorName} accepted an assignment: '{prop.Title}'.";
        await _notifications.NotifyRoleAsync("Broker", msg, "/Deals", actor?.Id, "assignment:accepted");
      }
      catch { }

      TempData["SuccessMessage"] = "Assignment accepted. Deal created in 'New'.";
      return RedirectToAction("Index", "Deals");
    }

    // POST: Properties/DeclineAssignment
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineAssignment(int id)
    {
      var prop = await _context.Properties.FindAsync(id);
      if (prop == null) return NotFound();

      var user = await _userManager.GetUserAsync(User);
      string? displayName = user?.UserName ?? user?.Email;
      try
      {
        var claims = user != null ? await _userManager.GetClaimsAsync(user) : null;
        var fullName = claims?.FirstOrDefault(c => c.Type == "FullName")?.Value;
        if (!string.IsNullOrWhiteSpace(fullName)) displayName = fullName;
      }
      catch { }

      if (!string.IsNullOrWhiteSpace(prop.Agent) && !string.IsNullOrWhiteSpace(displayName) && !prop.Agent.Equals(displayName, StringComparison.OrdinalIgnoreCase))
      {
        return Forbid();
      }

      // Enforce monthly decline limit
      try
      {
        var settings = await _context.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        var maxDeclines = settings?.MaxDeclinesPerAgentPerMonth ?? 3;
        if (user != null && maxDeclines > 0)
        {
          var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
          var end = start.AddMonths(1);
          var declinesThisMonth = await _context.Notifications.CountAsync(n => n.Type == "assignment:declined" && n.RecipientUserId == user.Id && n.CreatedAtUtc >= start && n.CreatedAtUtc < end);
          if (declinesThisMonth >= maxDeclines)
          {
            TempData["ErrorMessage"] = $"You have reached the monthly decline limit of {maxDeclines}.";
            return RedirectToAction("Index", "PendingAssignments");
          }
        }
      }
      catch { }

      // Decline -> unassign and activate back to properties pool
      prop.Agent = null;
      prop.ListingStatus = "Active";
      _context.Properties.Update(prop);
      await _context.SaveChangesAsync();

      // Track decline for stats and notify
      try
      {
        if (user != null)
        {
          await _notifications.NotifyUserAsync(user.Id, $"You declined an assignment: '{prop.Title}'.", "/PendingAssignments", user.Id, "assignment:declined");
          await _notifications.NotifyRoleAsync("Broker", $"{displayName ?? "Agent"} declined an assignment: '{prop.Title}'.", "/Properties", user.Id, "assignment:declined-broadcast");
        }
      }
      catch { }

      TempData["SuccessMessage"] = "Assignment declined.";
      return RedirectToAction("Index", "PendingAssignments");
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
            IEnumerable<ApplicationUser> filteredUsers = usersInRole;

            if (!string.IsNullOrWhiteSpace(q))
            {
              var qLower = q.ToLowerInvariant();
              filteredUsers = usersInRole.Where(u =>
                  (!string.IsNullOrEmpty(u.UserName) && u.UserName.ToLowerInvariant().Contains(qLower)) ||
                  (!string.IsNullOrEmpty(u.Email) && u.Email.ToLowerInvariant().Contains(qLower))
              );
            }

            var userAgents = new List<AgentDto>();
            foreach (var u in filteredUsers)
            {
              string? fullName = null;
              try
              {
                var claims = await _userManager.GetClaimsAsync(u);
                fullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value;
              }
              catch { }
              var display = !string.IsNullOrWhiteSpace(fullName)
                  ? fullName
                  : (!string.IsNullOrEmpty(u.UserName) ? u.UserName : (u.Email ?? u.Id));
              userAgents.Add(new AgentDto { Id = $"u:{u.Id}", Name = display });
            }

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

      return Json(new
      {
        totalCount = count,
        properties = properties.Select(p => new
        {
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

    private static DateTime NormalizeToUtc(DateTime dt)
    {
      if (dt.Kind == DateTimeKind.Utc) return dt;
      if (dt.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
      return dt.ToUniversalTime();
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
