using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class ArchivesController : Controller
    {
        private readonly AppDbContext _context;

        public ArchivesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? q = null)
        {
            var archivedQuery = _context.Deals
                .Include(d => d.Property)
                .Where(d => d.Status == "Archived");

            var closedQuery = _context.Deals
                .Include(d => d.Property)
                .Where(d => d.Status == "Closed");

            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim();
                archivedQuery = archivedQuery.Where(d =>
                    (d.Title != null && EF.Functions.ILike(d.Title, "%" + kw + "%")) ||
                    (d.AgentName != null && EF.Functions.ILike(d.AgentName, "%" + kw + "%")) ||
                    (d.ClientName != null && EF.Functions.ILike(d.ClientName, "%" + kw + "%")) ||
                    (d.Property != null && (
                        (d.Property.Title != null && EF.Functions.ILike(d.Property.Title, "%" + kw + "%")) ||
                        (d.Property.Address != null && EF.Functions.ILike(d.Property.Address, "%" + kw + "%"))
                    )));

                closedQuery = closedQuery.Where(d =>
                    (d.Title != null && EF.Functions.ILike(d.Title, "%" + kw + "%")) ||
                    (d.AgentName != null && EF.Functions.ILike(d.AgentName, "%" + kw + "%")) ||
                    (d.ClientName != null && EF.Functions.ILike(d.ClientName, "%" + kw + "%")) ||
                    (d.Property != null && (
                        (d.Property.Title != null && EF.Functions.ILike(d.Property.Title, "%" + kw + "%")) ||
                        (d.Property.Address != null && EF.Functions.ILike(d.Property.Address, "%" + kw + "%"))
                    )));
            }

            var archived = await archivedQuery
                .OrderByDescending(d => d.LastUpdated)
                .ToListAsync();
            var closed = await closedQuery
                .OrderByDescending(d => d.LastUpdated)
                .ToListAsync();

            ViewBag.Archived = archived;
            ViewBag.Closed = closed;
            ViewData["Title"] = "Archives";
            return View();
        }
    }
}

