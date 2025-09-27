using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using RealEstateCRM.Services.Notifications;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Text;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class DealsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly INotificationService _notifications;
        private readonly IEmailSender _emailSender;
        private readonly RealEstateCRM.Services.ContractPdfGenerator _pdfGen;

        public DealsController(AppDbContext context, UserManager<IdentityUser> userManager, INotificationService notifications, IEmailSender emailSender, RealEstateCRM.Services.ContractPdfGenerator pdfGen)
        {
            _context = context;
            _userManager = userManager;
            _notifications = notifications;
            _emailSender = emailSender;
            _pdfGen = pdfGen;
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

        // View-only JSON summary for a deal (used by board modal)
        [HttpGet]
        public async Task<IActionResult> DealSummary(int id)
        {
            var deal = await _context.Deals.Include(d => d.Property).FirstOrDefaultAsync(d => d.Id == id);
            if (deal == null) return NotFound();

            string? image = null;
            if (deal.Property != null)
            {
                image = !string.IsNullOrWhiteSpace(deal.Property.ImagePath)
                    ? Url.Content(deal.Property.ImagePath)
                    : Url.Content("~/assets/images/property-placeholder.jpg");
            }

            var payload = new
            {
                id = deal.Id,
                title = string.IsNullOrWhiteSpace(deal.Property?.Title) ? deal.Title : deal.Property!.Title,
                dealTitle = deal.Title,
                description = deal.Description,
                status = deal.Status,
                agent = deal.AgentName,
                client = deal.ClientName,
                offer = deal.OfferAmount,
                created = deal.CreatedDate,
                updated = deal.LastUpdated,
                property = deal.Property == null ? null : new
                {
                    id = deal.Property.Id,
                    title = deal.Property.Title,
                    address = deal.Property.Address,
                    price = deal.Property.Price,
                    type = deal.Property.PropertyType,
                    bedrooms = deal.Property.Bedrooms ?? 0,
                    bathrooms = deal.Property.Bathrooms ?? 0,
                    area = deal.Property.Area ?? 0,
                    image
                }
            };

            return Json(payload);
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
                // Require a client selection for every new deal
                if (string.IsNullOrWhiteSpace(request.ClientName))
                {
                    TempData["ErrorMessage"] = "Please select a client before creating a deal.";
                    return RedirectToAction(nameof(Index));
                }

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
                // Enforce transition rules
                var fromStatus = deal.Status ?? "";
                if (string.Equals(newStatus, "ContractDraft", StringComparison.OrdinalIgnoreCase))
                {
                    // Only allowed via SetOffer/CreateOffer; block manual drag
                    return BadRequest();
                }
                if (string.Equals(fromStatus, "New", StringComparison.OrdinalIgnoreCase))
                {
                    if (!(string.Equals(newStatus, "OfferMade", StringComparison.OrdinalIgnoreCase) || string.Equals(newStatus, "Negotiation", StringComparison.OrdinalIgnoreCase)))
                        return BadRequest();
                }
                else if (string.Equals(fromStatus, "Negotiation", StringComparison.OrdinalIgnoreCase))
                {
                    if (!(string.Equals(newStatus, "Negotiation", StringComparison.OrdinalIgnoreCase) || string.Equals(newStatus, "OfferMade", StringComparison.OrdinalIgnoreCase)))
                        return BadRequest();
                }
                if (string.Equals(newStatus, "UnderContract", StringComparison.OrdinalIgnoreCase) && !string.Equals(fromStatus, "ContractDraft", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest();
                }

                // Get the next display order for the new status
                var maxDisplayOrder = await _context.Deals
                    .Where(d => d.Status == newStatus)
                    .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

                deal.Status = newStatus;
                deal.DisplayOrder = maxDisplayOrder + 1;
                deal.LastUpdated = DateTime.UtcNow;

                // If moved into UnderContract, seed default deadlines (from AgencySettings) if none exist
                if (string.Equals(newStatus, "UnderContract", StringComparison.OrdinalIgnoreCase))
                {
                    var hasDeadlines = await _context.DealDeadlines.AnyAsync(dd => dd.DealId == deal.Id);
                    if (!hasDeadlines)
                    {
                        try
                        {
                            var now = DateTime.UtcNow.Date;
                            var cfg = await _context.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
                            int insp = cfg?.InspectionDays ?? 7;
                            int appr = cfg?.AppraisalDays ?? 14;
                            int loan = cfg?.LoanCommitmentDays ?? 21;
                            int clos = cfg?.ClosingDays ?? 30;
                            var list = new List<DealDeadline>
                            {
                                new DealDeadline { DealId = deal.Id, Type = "Inspection", DueDate = now.AddDays(insp) },
                                new DealDeadline { DealId = deal.Id, Type = "Appraisal", DueDate = now.AddDays(appr) },
                                new DealDeadline { DealId = deal.Id, Type = "LoanCommitment", DueDate = now.AddDays(loan) },
                                new DealDeadline { DealId = deal.Id, Type = "Closing", DueDate = now.AddDays(clos) }
                            };
                            _context.DealDeadlines.AddRange(list);
                            await _context.SaveChangesAsync();
                        }
                        catch { }
                    }
                    // Attempt to send contract packet to client
                    try { await TryEmailContractPacket(deal.Id); } catch { }
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            return BadRequest();
        }

        // Set or edit an offer for a deal and move it to ContractDraft (offers only in Negotiation)
        [HttpPost]
        public async Task<IActionResult> SetOffer(int dealId, decimal offerAmount)
        {
            var deal = await _context.Deals.Include(d => d.Property).FirstOrDefaultAsync(d => d.Id == dealId);
            if (deal == null) return NotFound();

            if (!string.Equals(deal.Status, "Negotiation", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Offers can only be set in the Negotiation stage.");

            // Validate against property price if known (10% below up to 100%)
            if (deal.Property != null)
            {
                var max = deal.Property.Price;
                var min = Math.Round(max * 0.9m, 2, MidpointRounding.AwayFromZero);
                if (offerAmount < min)
                    return BadRequest($"Minimum required offer: {min:N0}");
                if (offerAmount > max)
                    return BadRequest($"Maximum allowed offer: {max:N0}");
            }

            // Move to ContractDraft column
            var maxDisplayOrder = await _context.Deals
                .Where(d => d.Status == "ContractDraft")
                .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

            deal.OfferAmount = offerAmount;
            deal.Status = "ContractDraft";
            deal.DisplayOrder = maxDisplayOrder + 1;
            deal.LastUpdated = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok();
        }

        // --- Offers API ---
        [HttpGet]
        public async Task<IActionResult> GetOffers(int dealId)
        {
            var offers = await _context.Offers
                .Where(o => o.DealId == dealId)
                .OrderByDescending(o => o.CreatedAtUtc)
                .Select(o => new { o.Id, o.Amount, o.Status, o.FinancingType, o.EarnestMoney, o.CloseDate, o.Notes, o.CreatedAtUtc })
                .ToListAsync();
            return Json(offers);
        }

        [HttpPost]
        public async Task<IActionResult> CreateOffer(int dealId, decimal amount, string? financingType, decimal? earnestMoney, DateTime? closeDate, string? notes)
        {
            var deal = await _context.Deals.Include(d => d.Property).FirstOrDefaultAsync(d => d.Id == dealId);
            if (deal == null) return NotFound();

            // Offers can only be created while in Negotiation stage
            if (!string.Equals(deal.Status, "Negotiation", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Offers can only be added in the Negotiation stage.");

            if (deal.Property != null)
            {
                var max = deal.Property.Price;
                var min = Math.Round(max * 0.9m, 2, MidpointRounding.AwayFromZero);
                if (amount < min) return BadRequest($"Minimum required offer: {min:N0}");
                if (amount > max) return BadRequest($"Maximum allowed offer: {max:N0}");
            }

            var offer = new Offer
            {
                DealId = dealId,
                Amount = amount,
                FinancingType = financingType,
                EarnestMoney = earnestMoney,
                CloseDate = closeDate,
                Notes = notes,
                Status = "Proposed",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _context.Offers.Add(offer);
            await _context.SaveChangesAsync();

            // After setting an offer, move the deal to ContractDraft
            var maxDisplayOrder = await _context.Deals
                .Where(d => d.Status == "ContractDraft")
                .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;
            deal.OfferAmount = amount;
            deal.Status = "ContractDraft";
            deal.DisplayOrder = maxDisplayOrder + 1;
            deal.LastUpdated = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> AcceptOffer(int offerId)
        {
            var offer = await _context.Offers.Include(o => o.Deal).ThenInclude(d => d.Property).FirstOrDefaultAsync(o => o.Id == offerId);
            if (offer == null) return NotFound();

            // Validate again against property price
            if (offer.Deal?.Property != null)
            {
                var max = offer.Deal.Property.Price;
                var min = Math.Round(max * 0.9m, 2, MidpointRounding.AwayFromZero);
                if (offer.Amount < min) return BadRequest($"Minimum required offer: {min:N0}");
                if (offer.Amount > max) return BadRequest($"Maximum allowed offer: {max:N0}");
            }

            // Mark other offers declined
            var others = _context.Offers.Where(o => o.DealId == offer.DealId && o.Id != offer.Id && o.Status != "Declined");
            await others.ForEachAsync(o => { o.Status = "Declined"; o.UpdatedAtUtc = DateTime.UtcNow; });

            offer.Status = "Accepted";
            offer.UpdatedAtUtc = DateTime.UtcNow;

            // Move deal to UnderContract and set OfferAmount
            var deal = offer.Deal!;
            var maxDisplayOrder = await _context.Deals.Where(d => d.Status == "UnderContract").MaxAsync(d => (int?)d.DisplayOrder) ?? 0;
            deal.Status = "UnderContract";
            deal.OfferAmount = offer.Amount;
            deal.DisplayOrder = maxDisplayOrder + 1;
            deal.LastUpdated = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            
            // Generate default deadlines (from AgencySettings)
            try
            {
                var now = DateTime.UtcNow.Date;
                var cfg = await _context.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
                int insp = cfg?.InspectionDays ?? 7;
                int appr = cfg?.AppraisalDays ?? 14;
                int loan = cfg?.LoanCommitmentDays ?? 21;
                int clos = cfg?.ClosingDays ?? 30;
                var list = new List<DealDeadline>
                {
                    new DealDeadline { DealId = deal.Id, Type = "Inspection", DueDate = now.AddDays(insp) },
                    new DealDeadline { DealId = deal.Id, Type = "Appraisal", DueDate = now.AddDays(appr) },
                    new DealDeadline { DealId = deal.Id, Type = "LoanCommitment", DueDate = now.AddDays(loan) },
                    new DealDeadline { DealId = deal.Id, Type = "Closing", DueDate = now.AddDays(clos) }
                };
                _context.DealDeadlines.AddRange(list);
                await _context.SaveChangesAsync();
            }
            catch { }

            // Try emailing contract to client as we enter UnderContract via accepted offer
            try { await TryEmailContractPacket(deal.Id); } catch { }

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> DeclineOffer(int offerId)
        {
            var offer = await _context.Offers.FindAsync(offerId);
            if (offer == null) return NotFound();
            offer.Status = "Declined";
            offer.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetDeadlines(int dealId)
        {
            var items = await _context.DealDeadlines
                .Where(d => d.DealId == dealId)
                .OrderBy(d => d.DueDate)
                .Select(d => new { d.Id, d.Type, d.DueDate, d.CompletedAtUtc, d.Notes })
                .ToListAsync();
            return Json(items);
        }

        // Save or update multiple deadlines for a deal (allows manual date/time edits)
        public class DeadlineDto
        {
            public int? Id { get; set; }
            public string Type { get; set; } = string.Empty;
            public DateTime DueDate { get; set; }
            public string? Notes { get; set; }
        }

        public class SaveDeadlinesRequest
        {
            public int DealId { get; set; }
            public List<DeadlineDto> Deadlines { get; set; } = new();
        }

        [HttpPost]
        public async Task<IActionResult> SaveDeadlines([FromBody] SaveDeadlinesRequest req)
        {
            if (req == null) return BadRequest("Invalid payload");
            var deal = await _context.Deals.FindAsync(req.DealId);
            if (deal == null) return NotFound("Deal not found");

            // Load existing deadlines for the deal once
            var existing = await _context.DealDeadlines.Where(d => d.DealId == req.DealId).ToListAsync();

            foreach (var incoming in req.Deadlines)
            {
                if (incoming == null || string.IsNullOrWhiteSpace(incoming.Type)) continue;

                DealDeadline? target = null;

                if (incoming.Id.HasValue)
                {
                    target = existing.FirstOrDefault(d => d.Id == incoming.Id.Value && d.DealId == req.DealId);
                }

                // If not found by Id, try by unique (DealId + Type)
                if (target == null)
                {
                    target = existing.FirstOrDefault(d => d.Type == incoming.Type);
                }

                if (target == null)
                {
                    target = new DealDeadline
                    {
                        DealId = req.DealId,
                        Type = incoming.Type,
                        DueDate = incoming.DueDate,
                        Notes = incoming.Notes
                    };
                    _context.DealDeadlines.Add(target);
                    existing.Add(target);
                }
                else
                {
                    target.Type = incoming.Type; // allow rename corrections
                    target.DueDate = incoming.DueDate;
                    target.Notes = incoming.Notes;
                }
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        // Mark/unmark a deadline as completed
        [HttpPost]
        public async Task<IActionResult> SetDeadlineCompleted(int id, bool completed)
        {
            var item = await _context.DealDeadlines.FindAsync(id);
            if (item == null) return NotFound();
            item.CompletedAtUtc = completed ? DateTime.UtcNow : null;
            await _context.SaveChangesAsync();
            return Ok();
        }

        // Move a deal to Closed status
        [HttpPost]
        public async Task<IActionResult> CloseDeal(int dealId)
        {
            var deal = await _context.Deals.FindAsync(dealId);
            if (deal == null) return NotFound();

            var maxDisplayOrder = await _context.Deals
                .Where(d => d.Status == "Closed")
                .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

            deal.Status = "Closed";
            deal.DisplayOrder = maxDisplayOrder + 1;
            deal.LastUpdated = DateTime.UtcNow;
            try
            {
                var user = await _userManager.GetUserAsync(User);
                deal.ClosedByUserId = user?.Id;
                deal.ClosedAtUtc = DateTime.UtcNow;
            }
            catch { }
            await _context.SaveChangesAsync();
            return Ok();
        }

        // Build and send a contract email to the client assigned to the deal.
        private async Task TryEmailContractPacket(int dealId)
        {
            var deal = await _context.Deals.Include(d => d.Property).FirstOrDefaultAsync(d => d.Id == dealId);
            if (deal == null) return;

            // Lookup client by name in Contacts
            Contact? client = null;
            if (!string.IsNullOrWhiteSpace(deal.ClientName))
            {
                client = await _context.Contacts.FirstOrDefaultAsync(c => c.IsActive && c.Name.ToLower() == deal.ClientName!.ToLower());
            }
            if (client == null || string.IsNullOrWhiteSpace(client.Email)) return; // nothing to send

            // Pull accepted or latest offer
            var offer = await _context.Offers
                .Where(o => o.DealId == deal.Id)
                .OrderByDescending(o => o.Status == "Accepted")
                .ThenByDescending(o => o.CreatedAtUtc)
                .FirstOrDefaultAsync();

            // Pull deadlines (Inspection/Appraisal/LoanCommitment/Closing)
            var deadlines = await _context.DealDeadlines
                .Where(d => d.DealId == deal.Id)
                .ToListAsync();

            var settings = await _context.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
            var brokerPct = settings?.BrokerCommissionPercent ?? 10m;
            var agentPct = settings?.AgentCommissionPercent ?? 5m;

            // Build an HTML contract summary tailored to this project
            string html = BuildContractHtml(deal, client, offer, deadlines, brokerPct, agentPct);

            // Persist a copy as an .html under the contact's documents for reference
            try
            {
                var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "contacts", client.Id.ToString());
                Directory.CreateDirectory(uploadsRoot);
                var fname = $"Contract_{deal.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.html";
                var full = Path.Combine(uploadsRoot, fname);
                await System.IO.File.WriteAllTextAsync(full, html, Encoding.UTF8);
            }
            catch { }

            var subject = $"Contract Packet - {deal.Property?.Title ?? deal.Title}";
            // Generate PDF and attach
            byte[] pdfBytes = Array.Empty<byte>();
            try { pdfBytes = _pdfGen.Generate(deal, client, offer, deadlines, brokerPct, agentPct); } catch { }

            // Save PDF to contact docs as well (if created)
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                try
                {
                    var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "contacts", client.Id.ToString());
                    Directory.CreateDirectory(uploadsRoot);
                    var pdfName = $"Contract_{deal.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
                    var pdfFull = Path.Combine(uploadsRoot, pdfName);
                    await System.IO.File.WriteAllBytesAsync(pdfFull, pdfBytes);
                }
                catch { }
            }

            bool sent = false;
            if (_emailSender is RealEstateCRM.Services.EmailSender concrete && pdfBytes != null && pdfBytes.Length > 0)
            {
                sent = await concrete.SendEmailWithAttachmentsAsync(client.Email!, subject, html, new[]
                {
                    (FileName: $"Contract_{deal.Id}.pdf", ContentType: "application/pdf", Data: pdfBytes)
                });
            }
            if (!sent)
            {
                // Fallback without attachment
                await _emailSender.SendEmailAsync(client.Email!, subject, html);
                sent = true; // best-effort
            }

            try { await _notifications.NotifyRoleAsync("Broker", $"Contract email {(sent ? "sent" : "failed")} to {client.Email} for deal #{deal.Id}", "/Deals", null, "ContractEmail"); } catch { }
        }

        private static string BuildContractHtml(Deal deal, Contact client, Offer? offer, List<DealDeadline> deadlines, decimal brokerPct, decimal agentPct)
        {
            var sb = new StringBuilder();
            var prop = deal.Property;
            string money(decimal? v) => v.HasValue ? $"₱ {v.Value:N0}" : "-";
            string dt(DateTime? d) => d.HasValue ? d.Value.ToString("MMM dd, yyyy") : "-";

            var insp = deadlines.FirstOrDefault(x => x.Type == "Inspection");
            var appr = deadlines.FirstOrDefault(x => x.Type == "Appraisal");
            var loan = deadlines.FirstOrDefault(x => x.Type == "LoanCommitment");
            var clos = deadlines.FirstOrDefault(x => x.Type == "Closing");

            sb.Append("<div style='font-family:Inter,Arial,sans-serif;color:#111;line-height:1.5;'>");
            sb.Append("<h2 style='margin:0 0 6px'>Real Estate Purchase Contract</h2>");
            sb.Append("<div style='font-size:12px;color:#555;margin-bottom:12px'>Generated by Homey CRM</div>");

            // Parties
            sb.Append("<h3 style='margin:16px 0 6px'>Parties</h3><table style='width:100%;font-size:14px'>");
            sb.Append($"<tr><td style='padding:4px 8px;width:30%;color:#666'>Buyer</td><td style='padding:4px 8px'>{System.Net.WebUtility.HtmlEncode(client.Name)} ({System.Net.WebUtility.HtmlEncode(client.Email ?? "-")})</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Agent</td><td style='padding:4px 8px'>{System.Net.WebUtility.HtmlEncode(deal.AgentName ?? "-")}</td></tr>");
            sb.Append("</table>");

            // Property
            sb.Append("<h3 style='margin:16px 0 6px'>Property</h3><table style='width:100%;font-size:14px'>");
            sb.Append($"<tr><td style='padding:4px 8px;width:30%;color:#666'>Title</td><td style='padding:4px 8px'>{System.Net.WebUtility.HtmlEncode(prop?.Title ?? deal.Title)}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Address</td><td style='padding:4px 8px'>{System.Net.WebUtility.HtmlEncode(prop?.Address ?? "-")}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Type</td><td style='padding:4px 8px'>{System.Net.WebUtility.HtmlEncode(prop?.PropertyType ?? "-")}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>List Price</td><td style='padding:4px 8px'>{money(prop?.Price)}</td></tr>");
            sb.Append("</table>");

            // Financials
            sb.Append("<h3 style='margin:16px 0 6px'>Financial Terms</h3><table style='width:100%;font-size:14px'>");
            sb.Append($"<tr><td style='padding:4px 8px;width:30%;color:#666'>Offer Amount</td><td style='padding:4px 8px'>{money(offer?.Amount ?? deal.OfferAmount)}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Financing</td><td style='padding:4px 8px'>{System.Net.WebUtility.HtmlEncode(offer?.FinancingType ?? "-")}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Earnest Money</td><td style='padding:4px 8px'>{money(offer?.EarnestMoney)}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Proposed Close Date</td><td style='padding:4px 8px'>{dt(offer?.CloseDate)}</td></tr>");
            sb.Append("</table>");

            // Deadlines
            sb.Append("<h3 style='margin:16px 0 6px'>Deadlines</h3><table style='width:100%;font-size:14px'>");
            sb.Append($"<tr><td style='padding:4px 8px;width:30%;color:#666'>Inspection</td><td style='padding:4px 8px'>{dt(insp?.DueDate)}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Appraisal</td><td style='padding:4px 8px'>{dt(appr?.DueDate)}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Loan Commitment</td><td style='padding:4px 8px'>{dt(loan?.DueDate)}</td></tr>");
            sb.Append($"<tr><td style='padding:4px 8px;color:#666'>Closing</td><td style='padding:4px 8px'>{dt(clos?.DueDate)}</td></tr>");
            sb.Append("</table>");

            // Commissions note (transparency)
            sb.Append($"<div style='margin-top:16px;font-size:12px;color:#666'>Commission disclosure: Broker {brokerPct}% • Agent {agentPct}% (paid by seller/agency as applicable).</div>");

            // Boilerplate terms
            sb.Append("<h3 style='margin:16px 0 6px'>Standard Terms</h3>");
            sb.Append("<ul style='margin:0 0 12px 16px;font-size:13px;color:#333'>");
            sb.Append("<li>Offer is contingent upon satisfactory property inspection by the Inspection deadline.</li>");
            sb.Append("<li>Financing contingency applies through the Loan Commitment deadline.</li>");
            sb.Append("<li>Earnest money is held in escrow and credited at closing; subject to contingency terms.</li>");
            sb.Append("<li>All parties agree to act in good faith to complete closing by the specified Closing date.</li>");
            sb.Append("</ul>");

            sb.Append("<div style='margin-top:24px;font-size:12px;color:#666'>This email contains your contract summary. Keep for your records.</div>");
            sb.Append("</div>");

            return sb.ToString();
        }

        // Archive/unarchive a deal (uses simple status swap to avoid schema changes)
        [HttpPost]
        public async Task<IActionResult> ArchiveDeal(int dealId)
        {
            var deal = await _context.Deals.FindAsync(dealId);
            if (deal == null) return NotFound();

            var maxDisplayOrder = await _context.Deals
                .Where(d => d.Status == "Archived")
                .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

            deal.Status = "Archived";
            deal.DisplayOrder = maxDisplayOrder + 1;
            deal.LastUpdated = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> UnarchiveDeal(int dealId)
        {
            var deal = await _context.Deals.FindAsync(dealId);
            if (deal == null) return NotFound();

            // Return to Closed column when unarchiving
            var maxDisplayOrder = await _context.Deals
                .Where(d => d.Status == "Closed")
                .MaxAsync(d => (int?)d.DisplayOrder) ?? 0;

            deal.Status = "Closed";
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
