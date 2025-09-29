using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Services.Notifications;
using RealEstateCRM.Services.Logging;
using System.Text.Json;
using RealEstateCRM.Models;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    public class ArchivesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notifications;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAppLogger _appLogger;

        public ArchivesController(AppDbContext context, INotificationService notifications, UserManager<ApplicationUser> userManager, IAppLogger appLogger)
        {
            _context = context;
            _notifications = notifications;
            _userManager = userManager;
            _appLogger = appLogger;
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

            // Build archive metadata from notifications (reason, retention, hold, timestamps)
            var ids = archived.Select(d => d.Id).Concat(closed.Select(d => d.Id)).Distinct().ToList();
            var meta = await _context.Notifications
                .Where(n => n.Type == "ArchiveAudit")
                .OrderByDescending(n => n.CreatedAtUtc)
                .ToListAsync();
            var dict = new Dictionary<int, ArchiveMeta>();
            foreach (var n in meta)
            {
                try
                {
                    var doc = JsonDocument.Parse(n.Message);
                    if (!doc.RootElement.TryGetProperty("dealId", out var idEl)) continue;
                    var id = idEl.GetInt32();
                    if (!ids.Contains(id)) continue;
                    var action = doc.RootElement.GetProperty("action").GetString() ?? string.Empty;
                    if (!dict.TryGetValue(id, out var m)) m = new ArchiveMeta();
                    if (action == "ARCHIVE")
                    {
                        m.ArchivedAtUtc = n.CreatedAtUtc;
                        if (doc.RootElement.TryGetProperty("reason", out var r)) m.Reason = r.GetString();
                        if (doc.RootElement.TryGetProperty("retentionDays", out var rd)) m.RetentionDays = rd.GetInt32();
                    }
                    else if (action == "HOLD_ON") m.OnHold = true;
                    else if (action == "HOLD_OFF") m.OnHold = false;
                    dict[id] = m;
                }
                catch { }
            }

            ViewBag.Archived = archived;
            ViewBag.Closed = closed;
            ViewBag.Meta = dict;
            ViewBag.Closed = closed;
            ViewData["Title"] = "Archives";
            return View();
        }

        // ===== Bulk operations =====
        [HttpPost]
        [Authorize(Roles = "Broker")]
        public async Task<IActionResult> BulkArchive([FromForm] string dealIds, [FromForm] string? reason, [FromForm] int? retentionDays)
        {
            var ids = ParseIds(dealIds);
            if (ids.Count == 0) return BadRequest("No ids");
            var deals = await _context.Deals.Where(d => ids.Contains(d.Id)).ToListAsync();
            var maxOrder = await _context.Deals.Where(d => d.Status == "Archived").MaxAsync(d => (int?)d.DisplayOrder) ?? 0;
            foreach (var d in deals)
            {
                d.Status = "Archived";
                d.DisplayOrder = ++maxOrder;
                d.LastUpdated = DateTime.UtcNow;
                await LogAuditAsync(d.Id, "ARCHIVE", reason, retentionDays);
            }
            await _context.SaveChangesAsync();
            try { await _appLogger.LogAsync("AUDIT", "Archive", $"Archived {deals.Count} deal(s)", new { ids, reason, retentionDays }); } catch { }
            return Ok(new { ok = true, count = deals.Count });
        }

        [HttpPost]
        [Authorize(Roles = "Broker")]
        public async Task<IActionResult> BulkUnarchive([FromForm] string dealIds)
        {
            var ids = ParseIds(dealIds);
            if (ids.Count == 0) return BadRequest("No ids");
            var deals = await _context.Deals.Where(d => ids.Contains(d.Id) && d.Status == "Archived").ToListAsync();
            var maxOrder = await _context.Deals.Where(d => d.Status == "Closed").MaxAsync(d => (int?)d.DisplayOrder) ?? 0;
            foreach (var d in deals)
            {
                d.Status = "Closed";
                d.DisplayOrder = ++maxOrder;
                d.LastUpdated = DateTime.UtcNow;
                await LogAuditAsync(d.Id, "UNARCHIVE", null, null);
            }
            await _context.SaveChangesAsync();
            try { await _appLogger.LogAsync("AUDIT", "Archive", $"Unarchived {deals.Count} deal(s)", new { ids }); } catch { }
            return Ok(new { ok = true, count = deals.Count });
        }

        [HttpPost]
        [Authorize(Roles = "Broker")]
        public async Task<IActionResult> SetHold([FromForm] string dealIds, [FromForm] bool hold)
        {
            var ids = ParseIds(dealIds);
            foreach (var id in ids)
            {
                await LogAuditAsync(id, hold ? "HOLD_ON" : "HOLD_OFF", null, null);
            }
            try { await _appLogger.LogAsync("AUDIT", "Archive", hold ? "Hold enabled for deals" : "Hold disabled for deals", new { ids }); } catch { }
            return Ok(new { ok = true, hold });
        }

        [HttpPost]
        [Authorize(Roles = "Broker")]
        public async Task<IActionResult> BulkPurge([FromForm] string dealIds, [FromForm] string confirm)
        {
            if (!string.Equals(confirm, "PURGE", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Confirm with PURGE");
            var ids = ParseIds(dealIds);
            int purged = 0, held = 0;
            foreach (var id in ids)
            {
                if (await IsOnHold(id)) { held++; continue; }
                var deadlines = _context.DealDeadlines.Where(x => x.DealId == id);
                _context.DealDeadlines.RemoveRange(deadlines);
                var offers = _context.Offers.Where(x => x.DealId == id);
                _context.Offers.RemoveRange(offers);
                var deal = await _context.Deals.FindAsync(id);
                if (deal != null) _context.Deals.Remove(deal);
                await LogAuditAsync(id, "PURGE", null, null);
                purged++;
            }
            await _context.SaveChangesAsync();
            try { await _appLogger.LogAsync("AUDIT", "Archive", $"Purged {purged} deal(s)", new { ids, held }); } catch { }
            return Ok(new { ok = true, purged, held });
        }

        [HttpGet]
        [Authorize(Roles = "Broker")]
        public async Task<IActionResult> ExportCsv(string? ids)
        {
            var sel = ParseIds(ids ?? string.Empty);
            var q = _context.Deals.Include(d => d.Property).Where(d => d.Status == "Archived" || d.Status == "Closed");
            if (sel.Count > 0) q = q.Where(d => sel.Contains(d.Id));
            var data = await q.OrderByDescending(d => d.LastUpdated).ToListAsync();
            var lines = new List<string> { "Id,Status,Title,Property,Agent,Client,Offer,LastUpdated" };
            foreach (var d in data)
            {
                string csv = string.Join(',', new[] {
                    d.Id.ToString(),
                    CsvEscape(d.Status),
                    CsvEscape(d.Title),
                    CsvEscape(d.Property?.Title),
                    CsvEscape(d.AgentName),
                    CsvEscape(d.ClientName),
                    d.OfferAmount?.ToString("0.##") ?? "",
                    d.LastUpdated?.ToString("s") ?? ""
                });
                lines.Add(csv);
            }
            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));
            return File(bytes, "text/csv", "archives.csv");
        }

        [HttpPost]
        [Authorize(Roles = "Broker")]
        public async Task<IActionResult> AutoArchiveClosed([FromForm] int olderThanDays = 90)
        {
            var limit = DateTime.UtcNow.AddDays(-olderThanDays);
            var targets = await _context.Deals
                .Where(d => d.Status == "Closed" && (d.ClosedAtUtc ?? d.LastUpdated) <= limit)
                .ToListAsync();
            var maxOrder = await _context.Deals.Where(d => d.Status == "Archived").MaxAsync(d => (int?)d.DisplayOrder) ?? 0;
            foreach (var d in targets)
            {
                d.Status = "Archived";
                d.DisplayOrder = ++maxOrder;
                d.LastUpdated = DateTime.UtcNow;
                await LogAuditAsync(d.Id, "ARCHIVE", $"Auto-archived (>={olderThanDays} days)", olderThanDays);
            }
            await _context.SaveChangesAsync();
            try { await _appLogger.LogAsync("AUDIT", "Archive", $"Auto-archived closed deals older than {olderThanDays} days", new { count = targets.Count }); } catch { }
            return Ok(new { ok = true, count = targets.Count });
        }

        private List<int> ParseIds(string csv)
        {
            return (csv ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var i) ? i : (int?)null)
                .Where(i => i.HasValue)
                .Select(i => i!.Value)
                .Distinct()
                .ToList();
        }

        private async Task LogAuditAsync(int dealId, string action, string? reason, int? retentionDays)
        {
            var user = await _userManager.GetUserAsync(User);
            var msg = JsonSerializer.Serialize(new
            {
                dealId,
                action,
                reason = reason ?? string.Empty,
                retentionDays = retentionDays ?? 0
            });
            await _notifications.NotifyRoleAsync("Broker", msg, $"/Deals/All#deal-{dealId}", user?.Id, "ArchiveAudit");
        }

        private async Task<bool> IsOnHold(int dealId)
        {
            var n = await _context.Notifications
                .Where(n => n.Type == "ArchiveAudit")
                .OrderByDescending(n => n.CreatedAtUtc)
                .ToListAsync();
            foreach (var rec in n)
            {
                try
                {
                    var doc = JsonDocument.Parse(rec.Message);
                    if (doc.RootElement.GetProperty("dealId").GetInt32() != dealId) continue;
                    var action = doc.RootElement.GetProperty("action").GetString();
                    if (action == "HOLD_ON") return true;
                    if (action == "HOLD_OFF") return false;
                }
                catch { }
            }
            return false;
        }

        private static string CsvEscape(string? s)
        {
            var v = s ?? string.Empty;
            if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
            {
                v = v.Replace("\"", "\"\"");
                return "\"" + v + "\"";
            }
            return v;
        }

        public class ArchiveMeta
        {
            public DateTime? ArchivedAtUtc { get; set; }
            public string? Reason { get; set; }
            public int? RetentionDays { get; set; }
            public bool OnHold { get; set; }
        }
    }
}
