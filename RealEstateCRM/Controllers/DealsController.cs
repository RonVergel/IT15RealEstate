using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class DealsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public DealsController(AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            // Get all deals with their associated properties
            var deals = await _context.Deals
                .Include(d => d.Property)
                .OrderBy(d => d.DisplayOrder)
                .ThenBy(d => d.CreatedDate)
                .ToListAsync();

            // Group deals by status for the view
            ViewBag.DealsByStatus = deals.GroupBy(d => d.Status).ToDictionary(g => g.Key, g => g.ToList());

            return View();
        }

        // API endpoint to get available properties for the modal
        [HttpGet]
        public async Task<IActionResult> GetAvailableProperties()
        {
            var properties = await _context.Properties
                .Where(p => p.ListingStatus == "Active") // Only show active properties
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
            var result = agents.Select(u => new
            {
                id = u.Id,
                name = string.IsNullOrWhiteSpace(u.UserName) ? u.Email : u.UserName,
                email = u.Email
            }).ToList();
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
                // Get the next display order for "New" status
                var maxDisplayOrder = await _context.Deals
                    .Where(d => d.Status == "New")
                    .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

                var deal = new Deal
                {
                    PropertyId = request.PropertyId,
                    Title = request.Title,
                    Description = request.Description,
                    AgentName = request.AgentName,
                    ClientName = request.ClientName,
                    OfferAmount = request.OfferAmount,
                    Status = "New", // Always start in "New" column
                    DisplayOrder = maxDisplayOrder + 1,
                    CreatedDate = DateTime.Now,
                    LastUpdated = DateTime.Now
                };

                _context.Deals.Add(deal);
                await _context.SaveChangesAsync();

                // Set success message in TempData
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
                deal.LastUpdated = DateTime.Now;
                
                await _context.SaveChangesAsync();
                return Ok();
            }
            return BadRequest();
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
