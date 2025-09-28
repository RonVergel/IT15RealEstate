using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using RealEstateCRM.Services.Notifications;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser> _userManager;
        private readonly INotificationService _notifications;

        public DashboardController(AppDbContext context, Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser> userManager, INotificationService notifications)
        {
            _context = context;
            _userManager = userManager;
            _notifications = notifications;
        }

        public async Task<IActionResult> Index()
        {
            var pipelineData = await _context.Deals
                .GroupBy(d => d.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .OrderBy(x => x.Status)
                .ToListAsync();

            var dashboardData = new DashboardViewModel
            {
                // Get total count of properties
                TotalProperties = await _context.Properties.CountAsync(),

                // Get total count of deals
                TotalDeals = await _context.Deals.CountAsync(),

                // Get total count of contacts (excluding leads)
                TotalContacts = await _context.Contacts
                    .Where(c => c.IsActive && c.Type != "Lead")
                    .CountAsync(),

                // Get total count of leads
                TotalLeads = await _context.Leads
                    .Where(l => l.IsActive)
                    .CountAsync(),

                // Additional metrics
                ActiveProperties = await _context.Properties
                    .Where(p => p.ListingStatus == "Active")
                    .CountAsync(),

                TotalPropertyValue = await _context.Properties
                    .Where(p => p.ListingStatus == "Active")
                    .SumAsync(p => (decimal?)p.Price) ?? 0,

                AverageDaysOnMarket = await _context.Properties
                    .Where(p => p.ListingStatus == "Active" && p.DaysOnMarket.HasValue)
                    .AverageAsync(p => (double?)p.DaysOnMarket) ?? 0,

                AveragePricePerSQFT = await _context.Properties
                    .Where(p => p.ListingStatus == "Active" && p.PricePerSQFT.HasValue)
                    .AverageAsync(p => (decimal?)p.PricePerSQFT) ?? 0,

                // Deal status breakdown
                NewDeals = await _context.Deals.Where(d => d.Status == "New").CountAsync(),
                InProgressDeals = await _context.Deals.Where(d => d.Status == "In Progress").CountAsync(),
                ClosedDeals = await _context.Deals.Where(d => d.Status == "Closed").CountAsync(),

                // Contact type breakdown
                AgentContacts = await _context.Contacts
                    .Where(c => c.IsActive && c.Type == "Agent")
                    .CountAsync(),

                ClientContacts = await _context.Contacts
                    .Where(c => c.IsActive && c.Type == "Client")
                    .CountAsync(),

                // Recent activity
                RecentProperties = await _context.Properties
                    .OrderByDescending(p => p.ListingTime)
                    .Take(5)
                    .ToListAsync(),

                RecentDeals = await _context.Deals
                    .Include(d => d.Property)
                    .OrderByDescending(d => d.CreatedDate)
                    .Take(5)
                    .ToListAsync(),

                RecentContacts = await _context.Contacts
                    .Where(c => c.IsActive && c.Type != "Lead")
                    .OrderByDescending(c => c.DateCreated)
                    .Take(5)
                    .ToListAsync(),

                RecentLeads = await _context.Leads
                    .Where(l => l.IsActive)
                    .OrderByDescending(l => l.DateCreated)
                    .Take(5)
                    .ToListAsync(),
                
                // Deal Pipeline Chart Data
                DealStatusLabels = pipelineData.Select(p => p.Status).ToList(),
                DealsPerStatus = pipelineData.Select(p => p.Count).ToList()
            };

            // Load agency settings (commissions). Fallback to 10/5 if missing.
            var settings = await _context.AgencySettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
            var brokerPct = settings?.BrokerCommissionPercent ?? 10m;
            var agentPct = settings?.AgentCommissionPercent ?? 5m;

            // Date range for revenue (month/quarter/year via query)
            var range = (Request.Query["revRange"].FirstOrDefault() ?? "month").ToLowerInvariant();
            DateTime from = DateTime.UtcNow.AddDays(-30);
            string rangeLabel = "Last 30 days";
            if (range == "quarter") { from = DateTime.UtcNow.AddDays(-90); rangeLabel = "Last 90 days"; }
            else if (range == "year") { from = DateTime.UtcNow.AddDays(-365); rangeLabel = "Last 365 days"; }

            // Projected revenue from closed deals using agency settings within date range
            var closedDeals = await _context.Deals
                .Include(d => d.Property)
                .Where(d => d.Status == "Closed" && (d.ClosedAtUtc ?? d.LastUpdated) >= from)
                .ToListAsync();

            decimal broker = 0m, agent = 0m;
            foreach (var d in closedDeals)
            {
                var basePrice = d.OfferAmount ?? d.Property?.Price ?? 0m;
                if (basePrice <= 0) continue;
                broker += Math.Round(basePrice * (brokerPct / 100m), 2, MidpointRounding.AwayFromZero);
                agent += Math.Round(basePrice * (agentPct / 100m), 2, MidpointRounding.AwayFromZero);
            }

            dashboardData.ProjectedRevenueBroker = broker;
            dashboardData.ProjectedRevenueAgent = agent;
            dashboardData.ProjectedRevenueTotal = broker + agent;
            dashboardData.BrokerCommissionPercent = brokerPct;
            dashboardData.AgentCommissionPercent = agentPct;

            // Per-user (current broker) revenue within same date range
            var current = await _userManager.GetUserAsync(User);
            var currentId = current?.Id;
            string? displayName = current?.UserName ?? current?.Email;
            try
            {
                var fullName = User?.Claims?.FirstOrDefault(c => c.Type == "name")?.Value;
                if (!string.IsNullOrWhiteSpace(fullName)) displayName = fullName;
            }
            catch { }
            dashboardData.IsBroker = User?.Identity?.IsAuthenticated == true && await _userManager.IsInRoleAsync(current!, "Broker");
            dashboardData.RevenueRangeKey = range;
            dashboardData.RevenueRangeLabel = rangeLabel;

            if (dashboardData.IsBroker && !string.IsNullOrEmpty(currentId))
            {
                bool MatchAgent(string? agent)
                {
                    if (string.IsNullOrWhiteSpace(agent)) return false;
                    var a = agent.Trim();
                    return string.Equals(a, displayName, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(a, (current?.UserName ?? string.Empty), StringComparison.OrdinalIgnoreCase)
                           || string.Equals(a, (current?.Email ?? string.Empty), StringComparison.OrdinalIgnoreCase);
                }

                var myDeals = closedDeals.Where(d => d.ClosedByUserId == currentId || MatchAgent(d.AgentName));
                decimal myBroker = 0m;
                foreach (var d in myDeals)
                {
                    var basePrice = d.OfferAmount ?? d.Property?.Price ?? 0m;
                    if (basePrice <= 0) continue;
                    myBroker += Math.Round(basePrice * (brokerPct / 100m), 2, MidpointRounding.AwayFromZero);
                }
                dashboardData.MyBrokerRevenue = myBroker;
                dashboardData.AllBrokersRevenue = broker; // total broker revenue across all brokers
            }

            // ==============================
            // Monthly goal evaluation + notify brokers
            // ==============================
            var now = DateTime.UtcNow;
            var currentPeriod = now.Year * 100 + now.Month; // YYYYMM
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59, DateTimeKind.Utc);

            // Compute commissions for the current month regardless of selected UI range
            var monthDeals = await _context.Deals
                .Include(d => d.Property)
                .Where(d => d.Status == "Closed" && (d.ClosedAtUtc ?? d.LastUpdated) >= monthStart && (d.ClosedAtUtc ?? d.LastUpdated) <= monthEnd)
                .ToListAsync();
            decimal monthBroker = 0m, monthAgent = 0m;
            foreach (var d in monthDeals)
            {
                var basePrice = d.OfferAmount ?? d.Property?.Price ?? 0m;
                if (basePrice <= 0) continue;
                monthBroker += Math.Round(basePrice * (brokerPct / 100m), 2, MidpointRounding.AwayFromZero);
                monthAgent += Math.Round(basePrice * (agentPct / 100m), 2, MidpointRounding.AwayFromZero);
            }
            var monthTotal = monthBroker + monthAgent;

            // Build top monthly agent stats (by earnings then deals)
            var agentStats = monthDeals
                .Where(d => !string.IsNullOrWhiteSpace(d.AgentName))
                .GroupBy(d => d.AgentName!.Trim())
                .Select(g => new AgentMonthlyStat
                {
                    AgentName = g.Key,
                    DealsClosed = g.Count(),
                    Earnings = g.Sum(x =>
                    {
                        var basePrice = x.OfferAmount ?? x.Property?.Price ?? 0m;
                        return basePrice > 0 ? Math.Round(basePrice * (agentPct / 100m), 2, MidpointRounding.AwayFromZero) : 0m;
                    })
                })
                .OrderByDescending(s => s.Earnings)
                .ThenByDescending(s => s.DealsClosed)
                .Take(5)
                .ToList();
            dashboardData.TopMonthlyAgents = agentStats;

            if ((settings?.MonthlyRevenueGoal ?? 0m) > 0m)
            {
                var goal = settings!.MonthlyRevenueGoal;
                var achieved = monthTotal >= goal;

                // Notify achieved once per month when threshold crossed
                if (achieved && settings.LastNotifiedAchievedPeriod != currentPeriod)
                {
                    try
                    {
                        await _notifications.NotifyRoleAsync("Broker", $"Monthly goal reached! Revenue ₱{monthTotal:N0} / Goal ₱{goal:N0}", "/Dashboard", null, "GoalAchieved");
                        settings.LastNotifiedAchievedPeriod = currentPeriod;
                        await _context.SaveChangesAsync();
                    }
                    catch { }
                }
                // Notify behind once near month end if not achieved (last 5 days of month)
                else if (!achieved)
                {
                    var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
                    if (now.Day >= daysInMonth - 4 && settings.LastNotifiedBehindPeriod != currentPeriod)
                    {
                        try
                        {
                            await _notifications.NotifyRoleAsync("Broker", $"Monthly goal not met yet: ₱{monthTotal:N0} / ₱{goal:N0}.", "/Dashboard", null, "GoalBehind");
                            settings.LastNotifiedBehindPeriod = currentPeriod;
                            await _context.SaveChangesAsync();
                        }
                        catch { }
                    }
                }
            }

            // =========================
            // Preferred Property Type by Occupation
            // =========================
            // Build a combined list of people (contacts + leads) with occupations and names
            var contactPersons = await _context.Contacts
                .Where(c => c.IsActive && !string.IsNullOrEmpty(c.Occupation) && !string.IsNullOrEmpty(c.Name))
                .Select(c => new { c.Name, c.Occupation })
                .ToListAsync();

            var leadPersons = await _context.Leads
                .Where(l => l.IsActive && !string.IsNullOrEmpty(l.Occupation) && !string.IsNullOrEmpty(l.Name))
                .Select(l => new { l.Name, l.Occupation })
                .ToListAsync();

            var persons = contactPersons
                .Concat(leadPersons)
                .GroupBy(p => p.Occupation.Trim())
                .Select(g => new
                {
                    Occupation = g.Key,
                    Names = g.Select(x => x.Name.Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList()
                })
                .ToList();

            var occStats = new List<OccupationPropertyTypeStat>();

            foreach (var occ in persons)
            {
                var names = occ.Names;
                if (names == null || !names.Any()) continue;

                // Deal-based aggregation (join deals -> properties using PropertyId)
                var fromDealsQuery = _context.Deals
                    .Where(d => !string.IsNullOrEmpty(d.ClientName) && names.Contains(d.ClientName))
                    .Join(_context.Properties,
                          d => d.PropertyId,
                          p => p.Id,
                          (d, p) => p.PropertyType ?? "Unknown");

                var dealCounts = await fromDealsQuery
                    .GroupBy(pt => pt)
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .ToListAsync();

                // Fallback: properties where Agent matches person name
                var propCounts = await _context.Properties
                    .Where(p => !string.IsNullOrEmpty(p.Agent) && names.Contains(p.Agent))
                    .GroupBy(p => p.PropertyType ?? "Unknown")
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .ToListAsync();

                // Merge counts
                var merged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in dealCounts) merged[d.Type] = merged.GetValueOrDefault(d.Type) + d.Count;
                foreach (var p in propCounts) merged[p.Type] = merged.GetValueOrDefault(p.Type) + p.Count;

                var residential = merged.GetValueOrDefault("Residential");
                var commercial = merged.GetValueOrDefault("Commercial");
                var rawLand = merged.GetValueOrDefault("Raw Land");
                var unknown = merged.Where(kv => kv.Key != "Residential" && kv.Key != "Commercial" && kv.Key != "Raw Land").Sum(kv => kv.Value);

                var total = residential + commercial + rawLand + unknown;
                if (total == 0) continue; // skip occupations with no associated properties/deals

                occStats.Add(new OccupationPropertyTypeStat
                {
                    Occupation = occ.Occupation,
                    Residential = residential,
                    Commercial = commercial,
                    RawLand = rawLand,
                    Other = unknown,
                    Total = total
                });
            }

            // Keep top occupations (by total) to avoid huge payload — top 10
            dashboardData.PreferredPropertyTypesByOccupation = occStats
                .OrderByDescending(o => o.Total)
                .Take(10)
                .ToList();

            // =========================
            // Average client salary by monthly time window (last 12 months)
            // =========================
            var labels = new List<string>();
            var values = new List<decimal>();

            // Build last 12 month windows (from oldest -> newest)
            var monthWindows = Enumerable.Range(0, 12)
                .Select(i => DateTime.UtcNow.AddMonths(-11 + i))
                .Select(d => new { Year = d.Year, Month = d.Month, Label = d.ToString("MMM yyyy") })
                .ToList();

            var startDate = monthWindows.First();
            DateTime fromDate = new DateTime(startDate.Year, startDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // Query DB for averages per year/month for clients with salary
            var monthlyAvgQuery = await _context.Contacts
                .Where(c => c.IsActive && c.Type == "Client" && c.Salary.HasValue && c.DateCreated >= fromDate)
                .GroupBy(c => new { c.DateCreated.Year, c.DateCreated.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    AvgSalary = g.Average(x => (decimal?)x.Salary) ?? 0m
                })
                .ToListAsync();

            // Map results into the 12-month series (fill missing months with 0)
            foreach (var m in monthWindows)
            {
                labels.Add(m.Label);
                var found = monthlyAvgQuery.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
                values.Add(found != null ? Math.Round(found.AvgSalary, 2) : 0m);
            }

            dashboardData.AvgClientSalaryLabels = labels;
            dashboardData.AvgClientSalaryValues = values;

            // ==============================
            // Deal Conversion Rate (filterable by month)
            // ==============================
            // convMonth accepted formats: "YYYY-MM" or "YYYYMM"; default = current month
            var convParam = (Request.Query["convMonth"].FirstOrDefault() ?? string.Empty).Trim();
            DateTime nowLocal = DateTime.UtcNow;
            int y = nowLocal.Year, convMonth = nowLocal.Month;
            if (!string.IsNullOrWhiteSpace(convParam))
            {
                try
                {
                    if (convParam.Contains("-"))
                    {
                        var parts = convParam.Split('-', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && int.TryParse(parts[0], out var yy) && int.TryParse(parts[1], out var mm) && mm >= 1 && mm <= 12)
                        {
                            y = yy; convMonth = mm;
                        }
                    }
                    else if (convParam.Length == 6 && int.TryParse(convParam.Substring(0, 4), out var yy2) && int.TryParse(convParam.Substring(4, 2), out var mm2) && mm2 >= 1 && mm2 <= 12)
                    {
                        y = yy2; convMonth = mm2;
                    }
                }
                catch { }
            }

            var convStart = new DateTime(y, convMonth, 1, 0, 0, 0, DateTimeKind.Utc);
            var convEnd = convStart.AddMonths(1).AddSeconds(-1);

            var totalCreated = await _context.Deals.CountAsync(d => d.CreatedDate >= convStart && d.CreatedDate <= convEnd);
            var totalClosed = await _context.Deals.CountAsync(d => d.Status == "Closed" && (d.ClosedAtUtc ?? d.LastUpdated) >= convStart && (d.ClosedAtUtc ?? d.LastUpdated) <= convEnd);

            dashboardData.ConversionMonthKey = $"{y:0000}-{convMonth:00}";
            dashboardData.ConversionMonthLabel = new DateTime(y, convMonth, 1).ToString("MMMM yyyy");
            dashboardData.ConversionCreated = totalCreated;
            dashboardData.ConversionClosed = totalClosed;
            dashboardData.ConversionRate = totalCreated == 0 ? 0m : Math.Round((decimal)totalClosed * 100m / totalCreated, 2);

            // Simple 2-page pagination for dashboard sections
            var pageStr = Request.Query["page"].FirstOrDefault();
            int page = 1;
            if (!string.IsNullOrWhiteSpace(pageStr) && int.TryParse(pageStr, out var parsed))
                page = parsed;
            if (page < 1) page = 1; if (page > 2) page = 2;
            dashboardData.Page = page;
            dashboardData.TotalPages = 2;

            return View(dashboardData);
        }

        [Authorize]
        public async Task<IActionResult> AdminSettings()
        {
            // This action would require authentication
            return View();
        }
    }

    // Dashboard View Model
    public class DashboardViewModel
    {
        // Main counts
        public int TotalProperties { get; set; }
        public int TotalDeals { get; set; }
        public int TotalContacts { get; set; }
        public int TotalLeads { get; set; }

        // Additional metrics
        public int ActiveProperties { get; set; }
        public decimal TotalPropertyValue { get; set; }
        public double AverageDaysOnMarket { get; set; }
        public decimal AveragePricePerSQFT { get; set; }

        // Breakdown data
        public int NewDeals { get; set; }
        public int InProgressDeals { get; set; }
        public int ClosedDeals { get; set; }
        public int AgentContacts { get; set; }
        public int ClientContacts { get; set; }

        // Recent activity
        public List<Property> RecentProperties { get; set; } = new();
        public List<Deal> RecentDeals { get; set; } = new();
        public List<Contact> RecentContacts { get; set; } = new();
        public List<Lead> RecentLeads { get; set; } = new();

        // New: preferred property type by occupation (top occupations only)
        public List<OccupationPropertyTypeStat> PreferredPropertyTypesByOccupation { get; set; } = new();

        // New: average client salary series (monthly)
        public List<string> AvgClientSalaryLabels { get; set; } = new();
        public List<decimal> AvgClientSalaryValues { get; set; } = new();

        // Deal pipeline chart data (labels and counts)
        public List<string> DealStatusLabels { get; set; } = new();
        public List<int> DealsPerStatus { get; set; } = new();

        // Projected revenue (commissions) from closed deals
        public decimal ProjectedRevenueTotal { get; set; }
        public decimal ProjectedRevenueBroker { get; set; }
        public decimal ProjectedRevenueAgent { get; set; }
        public decimal BrokerCommissionPercent { get; set; } = 10m;
        public decimal AgentCommissionPercent { get; set; } = 5m;

        // Per-user revenue (for brokers)
        public bool IsBroker { get; set; }
        public string RevenueRangeKey { get; set; } = "month"; // month|quarter|year
        public string RevenueRangeLabel { get; set; } = "Last 30 days";
        public decimal MyBrokerRevenue { get; set; }
        public decimal AllBrokersRevenue { get; set; }

        // Top monthly agents (by agent earnings from closed deals this month)
        public List<AgentMonthlyStat> TopMonthlyAgents { get; set; } = new();

        // Deal conversion rate (per-month)
        public string ConversionMonthKey { get; set; } = string.Empty; // e.g., 2025-09
        public string ConversionMonthLabel { get; set; } = string.Empty; // e.g., September 2025
        public int ConversionCreated { get; set; }
        public int ConversionClosed { get; set; }
        public decimal ConversionRate { get; set; } // percentage 0..100

        // Pagination
        public int Page { get; set; } = 1;
        public int TotalPages { get; set; } = 2;
    }

    // Simple DTO for occupation -> property type counts
    public class OccupationPropertyTypeStat
    {
        public string Occupation { get; set; } = string.Empty;
        public int Residential { get; set; }
        public int Commercial { get; set; }
        public int RawLand { get; set; }
        public int Other { get; set; }
        public int Total { get; set; }
    }

    // Simple DTO for monthly agent performance
    public class AgentMonthlyStat
    {
        public string AgentName { get; set; } = string.Empty;
        public int DealsClosed { get; set; }
        public decimal Earnings { get; set; }
    }
}
