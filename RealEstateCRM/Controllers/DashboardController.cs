using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;
using System.Collections.Generic;
using System.Linq;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
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

            return View(dashboardData);
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
        
        // Properties for Analytics & Reporting page
        public decimal TotalSalesVolume { get; set; }
        public decimal AverageDealValue { get; set; }
        public int NewLeadsLast30Days { get; set; }
        public List<string> DealStatusLabels { get; set; } = new();
        public List<int> DealsPerStatus { get; set; } = new();
    }
}
