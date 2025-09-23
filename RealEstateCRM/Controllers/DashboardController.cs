using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace RealEstateCRM.Controllers
{
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        [AllowAnonymous]
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
                .Select(i => DateTime.Now.AddMonths(-11 + i))
                .Select(d => new { Year = d.Year, Month = d.Month, Label = d.ToString("MMM yyyy") })
                .ToList();

            var startDate = monthWindows.First();
            DateTime fromDate = new DateTime(startDate.Year, startDate.Month, 1);

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
}
