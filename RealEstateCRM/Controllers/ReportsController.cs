using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly AppDbContext _context;

        public ReportsController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            var viewModel = new ReportsViewModel
            {
                // Sales Performance KPIs
                TotalSalesVolume = await _context.Deals
                    .Where(d => d.Status == "Closed" && d.LastUpdated >= thirtyDaysAgo)
                    .SumAsync(d => d.OfferAmount) ?? 0,

                ClosedDealsCount = await _context.Deals
                    .Where(d => d.Status == "Closed" && d.LastUpdated >= thirtyDaysAgo)
                    .CountAsync(),

                // Lead Funnel KPIs
                NewLeadsCount = await _context.Leads
                    .Where(l => l.DateCreated >= thirtyDaysAgo)
                    .CountAsync(),

                // Property Metrics
                AvgDaysOnMarket = await _context.Properties
                    .Where(p => p.ListingStatus == "Active")
                    .AverageAsync(p => p.DaysOnMarket) ?? 0,

                TotalListingsValue = await _context.Properties
                    .Where(p => p.ListingStatus == "Active")
                    .SumAsync(p => p.Price)
            };

            // Calculate Average Deal Value safely
            viewModel.AverageDealValue = viewModel.ClosedDealsCount > 0
                ? viewModel.TotalSalesVolume / viewModel.ClosedDealsCount
                : 0;

            return View(viewModel);
        }
    }

    // ViewModel for the Reports page
    public class ReportsViewModel
    {
        public decimal TotalSalesVolume { get; set; }
        public int ClosedDealsCount { get; set; }
        public decimal AverageDealValue { get; set; }
        public int NewLeadsCount { get; set; }
        public double AvgDaysOnMarket { get; set; }
        public decimal TotalListingsValue { get; set; }
    }
}