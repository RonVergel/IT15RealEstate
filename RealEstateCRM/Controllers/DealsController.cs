using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using RealEstateCRM.Services.Notifications;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class DealsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly INotificationService _notifications;

        public DealsController(AppDbContext context, UserManager<IdentityUser> userManager, INotificationService notifications)
        {
            _context = context;
            _userManager = userManager;
            _notifications = notifications;
        }

        public async Task<IActionResult> Index()
        {
            // Determine current user's display name and roles
            var user = await _userManager.GetUserAsync(User);
            var isBroker = user != null && await _userManager.IsInRoleAsync(user, "Broker");
            var isAgent = user != null && await _userManager.IsInRoleAsync(user, "Agent");

            string? displayName = user?.UserName ?? user?.Email;
            try
            {
                var claims = user != null ? await _userManager.GetClaimsAsync(user) : null;
                var fullName = claims?.FirstOrDefault(c => c.Type == "FullName")?.Value;
                if (!string.IsNullOrWhiteSpace(fullName)) displayName = fullName;
            }
            catch { }

            // Query base with include
            var query = _context.Deals.Include(d => d.Property).AsQueryable();

            // Visibility rules:
            // - Agents: see only deals assigned to them (AgentName == displayName)
            // - Brokers: do NOT see deals assigned to other agents; see unassigned or their own
            if (isAgent && !isBroker)
            {
                query = query.Where(d => d.AgentName != null && d.AgentName.ToLower() == (displayName ?? string.Empty).ToLower());
            }
            else if (isBroker)
            {
                query = query.Where(d => string.IsNullOrEmpty(d.AgentName) || d.AgentName.ToLower() == (displayName ?? string.Empty).ToLower());
            }

            var deals = await query
                .OrderBy(d => d.DisplayOrder)
                .ThenBy(d => d.CreatedDate)
                .ToListAsync();

            // Group deals by status for the view
            ViewBag.DealsByStatus = deals.GroupBy(d => d.Status).ToDictionary(g => g.Key, g => g.ToList());

            return View();
        }

        // Broker-only: view all deals without agent-based filtering
        [Authorize(Roles = "Broker")]
        public async Task<IActionResult> All()
        {
            var deals = await _context.Deals
                .Include(d => d.Property)
                .OrderBy(d => d.DisplayOrder)
                .ThenBy(d => d.CreatedDate)
                .ToListAsync();

            ViewBag.DealsByStatus = deals
                .GroupBy(d => d.Status)
                .ToDictionary(g => g.Key, g => g.ToList());

            ViewData["Title"] = "All Deals";
            return View("Index");
        }

        // API endpoint to get available properties for the modal
        [HttpGet]
        public async Task<IActionResult> GetAvailableProperties()
        {
            // Show only properties that are Active AND not already assigned to any deal
            var properties = await _context.Properties
                .Where(p => p.ListingStatus == "Active" && !_context.Deals.Any(d => d.PropertyId == p.Id))
                .Select(p => new
                {
                    id = p.Id,
                    title = p.Title,
                    address = p.Address,
                    price = p.Price,
                    propertyType = p.PropertyType,
                    bedrooms = p.Bedrooms ?? 0,
                    bathrooms = p.Bathrooms ?? 0,
                    area = p.Area ?? 0
                })
                .ToListAsync();

            return Json(properties);
        }

        // New: return users that are in the "Agent" role
        [HttpGet]
        public async Task<IActionResult> GetAgents()
        {
            var agents = await _userManager.GetUsersInRoleAsync("Agent");
            var result = new List<object>();
            foreach (var u in agents)
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
                    : (!string.IsNullOrWhiteSpace(u.UserName) ? u.UserName : (u.Email ?? u.Id));

                result.Add(new { id = u.Id, name = display, email = u.Email });
            }
            return Json(result);
        }

        // New: return clients from Contacts table
        [HttpGet]
        public async Task<IActionResult> GetClients()
        {
            var clients = await _context.Contacts
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    email = c.Email
                })
                .ToListAsync();

            return Json(clients);
        }

        // Handle deal creation
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDeal([FromForm] CreateDealRequest request)
        {
            if (ModelState.IsValid)
            {
                // Validate offer amount against property price (min 90%, max 100%)
                var propForValidation = await _context.Properties.FindAsync(request.PropertyId);
                if (propForValidation == null)
                {
                    TempData["ErrorMessage"] = "Selected property was not found.";
                    return RedirectToAction(nameof(Index));
                }

                if (request.OfferAmount.HasValue)
                {
                    var price = propForValidation.Price;
                    var minAllowed = Math.Round(price * 0.9m, 2, MidpointRounding.AwayFromZero); // 10% below max
                    var maxAllowed = price;
                    var offer = request.OfferAmount.Value;

                    if (offer > maxAllowed || offer < minAllowed)
                    {
                        TempData["ErrorMessage"] = $"Offer must be between {minAllowed:N0} and {maxAllowed:N0} for '{propForValidation.Title}'.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                // If a Broker assigns an agent while creating a deal,
                // route it as a Pending Assignment for that agent instead of creating the deal now.
                var currentUser = await _userManager.GetUserAsync(User);
                var isBroker = currentUser != null && await _userManager.IsInRoleAsync(currentUser, "Broker");
                var hasAssignedAgent = !string.IsNullOrWhiteSpace(request.AgentName);

                if (isBroker && hasAssignedAgent)
                {
                    var prop = await _context.Properties.FindAsync(request.PropertyId);
                    if (prop != null)
                    {
                        prop.Agent = request.AgentName;
                        prop.ListingStatus = "Pending"; // Send to agent's pending assignments
                        _context.Properties.Update(prop);
                        await _context.SaveChangesAsync();

                        TempData["SuccessMessage"] = "Assignment sent to agent for acceptance.";

                        // Notify the assigned agent
                        try
                        {
                            var actor = await _userManager.GetUserAsync(User);
                            string actorName = actor?.UserName ?? actor?.Email ?? "Broker";
                            try
                            {
                                var claims = actor != null ? await _userManager.GetClaimsAsync(actor) : null;
                                var fullName = claims?.FirstOrDefault(c => c.Type == "FullName")?.Value;
                                if (!string.IsNullOrWhiteSpace(fullName)) actorName = fullName;
                            }
                            catch { }

                            var msg = $"{actorName} assigned you a property to review: '{prop.Title}'.";

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

                                    if (string.Equals(name, request.AgentName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        await _notifications.NotifyUserAsync(u.Id, msg, "/PendingAssignments", actor?.Id, "assignment:new");
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                        catch { }

                        // Do not send the broker to the agent-only Pending Assignments page
                        return RedirectToAction(nameof(Index));
                    }
                }

                // Otherwise, create the Deal directly. If offer was provided, place in OfferMade; else New.
                var initialStatus = request.OfferAmount.HasValue ? "OfferMade" : "New";
                var maxDisplayOrder = await _context.Deals
                    .Where(d => d.Status == initialStatus)
                    .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

                var deal = new Deal
                {
                    PropertyId = request.PropertyId,
                    Title = request.Title,
                    Description = request.Description,
                    AgentName = request.AgentName,
                    ClientName = request.ClientName,
                    OfferAmount = request.OfferAmount,
                    Status = initialStatus,
                    DisplayOrder = maxDisplayOrder + 1,
                    CreatedDate = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                };

                _context.Deals.Add(deal);
                await _context.SaveChangesAsync();

                // Notify creation
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

                    var msg = $"{actorName} created a deal: '{deal.Title}'.";
                    await _notifications.NotifyRoleAsync("Broker", msg, "/Deals", actor?.Id, "deal:create");

                    // Try notify assigned agent by matching FullName or UserName
                    if (!string.IsNullOrWhiteSpace(deal.AgentName))
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

                                if (string.Equals(name, deal.AgentName, StringComparison.OrdinalIgnoreCase))
                                {
                                    await _notifications.NotifyUserAsync(u.Id, msg, "/Deals", actor?.Id, "deal:create");
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                TempData["SuccessMessage"] = "Deal Added";
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Index));
        }

        // Move deal between columns
        [HttpPost]
        public async Task<IActionResult> MoveDeal(int dealId, string newStatus)
        {
            var deal = await _context.Deals.FindAsync(dealId);
            if (deal != null)
            {
                // Get the next display order for the new status
                var maxDisplayOrder = await _context.Deals
                    .Where(d => d.Status == newStatus)
                    .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

                deal.Status = newStatus;
                deal.DisplayOrder = maxDisplayOrder + 1;
                deal.LastUpdated = DateTime.UtcNow;
                
                await _context.SaveChangesAsync();
                return Ok();
            }
            return BadRequest();
        }

        // Set or edit an offer for a deal and move it to OfferMade
        [HttpPost]
        public async Task<IActionResult> SetOffer(int dealId, decimal offerAmount)
        {
            var deal = await _context.Deals.Include(d => d.Property).FirstOrDefaultAsync(d => d.Id == dealId);
            if (deal == null) return NotFound();

            // Validate against property price if known (10% below up to 100%)
            if (deal.Property != null)
            {
                var max = deal.Property.Price;
                var min = Math.Round(max * 0.9m, 2, MidpointRounding.AwayFromZero);
                if (offerAmount > max || offerAmount < min)
                {
                    return BadRequest($"Offer must be between {min:N0} and {max:N0}.");
                }
            }

            // Move to OfferMade column
            var maxDisplayOrder = await _context.Deals
                .Where(d => d.Status == "OfferMade")
                .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

            deal.OfferAmount = offerAmount;
            deal.Status = "OfferMade";
            deal.DisplayOrder = maxDisplayOrder + 1;
            deal.LastUpdated = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok();
        }
    }

    // DTO for deal creation
    public class CreateDealRequest
    {
        public int PropertyId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AgentName { get; set; }
        public string? ClientName { get; set; }
        public decimal? OfferAmount { get; set; }
    }
}
