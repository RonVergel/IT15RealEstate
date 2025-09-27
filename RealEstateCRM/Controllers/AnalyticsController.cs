using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using System.Text;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AnalyticsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("deal-status-distribution")]
        public async Task<IActionResult> GetDealStatusDistribution()
        {
            var data = new
            {
                InProgress = await _context.Deals.Where(d => d.Status == "In Progress").CountAsync(),
                Closed = await _context.Deals.Where(d => d.Status == "Closed").CountAsync(),
            };
            return Ok(data);
        }

        [HttpGet("contact-type-distribution")]
        public async Task<IActionResult> GetContactTypeDistribution()
        {
            var data = new
            {
                Agents = await _context.Contacts.Where(c => c.IsActive && c.Type == "Agent").CountAsync(),
                Clients = await _context.Contacts.Where(c => c.IsActive && c.Type == "Client").CountAsync(),
            };
            return Ok(data);
        }

        [HttpGet("deal-pipeline")]
        public async Task<IActionResult> GetDealPipeline()
        {
            var pipelineData = await _context.Deals
                .Where(d => d.Status != "Closed" && d.Status != "Archived") // Only active deals
                .GroupBy(d => d.Status)
                .Select(g => new
                {
                    StageName = g.Key,
                    DealCount = g.Count(),
                    TotalValue = g.Sum(d => d.OfferAmount) ?? 0
                })
                .ToListAsync();

            // Ensure all stages are present, even if they have 0 deals, and maintain order
            var stages = new[] { "New", "OfferMade", "Negotiation", "ContractDraft" };
            var result = stages.Select(stage =>
            {
                var data = pipelineData.FirstOrDefault(p => p.StageName == stage);
                return new
                {
                    StageName = stage,
                    DealCount = data?.DealCount ?? 0,
                    TotalValue = data?.TotalValue ?? 0
                };
            });

            return Ok(result);
        }

        [HttpGet("export-deal-pipeline")]
        public async Task<IActionResult> ExportDealPipeline()
        {
            var pipelineData = await _context.Deals
                .Where(d => d.Status != "Closed" && d.Status != "Archived")
                .GroupBy(d => d.Status)
                .Select(g => new
                {
                    StageName = g.Key,
                    DealCount = g.Count(),
                    TotalValue = g.Sum(d => d.OfferAmount) ?? 0
                })
                .ToListAsync();

            var stages = new[] { "New", "OfferMade", "Negotiation", "ContractDraft" };
            var dataToExport = stages.Select(stage =>
            {
                var data = pipelineData.FirstOrDefault(p => p.StageName == stage);
                return new
                {
                    StageName = stage,
                    DealCount = data?.DealCount ?? 0,
                    TotalValue = data?.TotalValue ?? 0
                };
            });

            var builder = new StringBuilder();
            builder.AppendLine("StageName,DealCount,TotalValue");

            foreach (var item in dataToExport)
            {
                builder.AppendLine($"{item.StageName},{item.DealCount},{item.TotalValue}");
            }

            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", $"deal-pipeline-{DateTime.UtcNow:yyyy-MM-dd}.csv");
        }

        [HttpGet("export-recent-deals")]
        public async Task<IActionResult> ExportRecentDeals()
        {
            var deals = await _context.Deals
                .Include(d => d.Property)
                .OrderByDescending(d => d.CreatedDate)
                .Take(20) // Or some other reasonable limit
                .ToListAsync();

            var builder = new StringBuilder();
            builder.AppendLine("Title,Property,Status,OfferAmount,CreatedDate");

            foreach (var deal in deals)
            {
                builder.AppendLine($"{deal.Title},{deal.Property?.Title},{deal.Status},{deal.OfferAmount},{deal.CreatedDate}");
            }

            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", $"recent-deals-{DateTime.UtcNow:yyyy-MM-dd}.csv");
        }
    }
}
