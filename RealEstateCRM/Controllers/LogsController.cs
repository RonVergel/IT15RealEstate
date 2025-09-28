using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;

namespace RealEstateCRM.Controllers
{
    [Authorize(Roles = "Broker")]
    public class LogsController : Controller
    {
        private readonly AppDbContext _db;
        public LogsController(AppDbContext db) { _db = db; }

        [HttpGet]
        public async Task<IActionResult> Index(string? level = null, string? q = null, int take = 200, DateTime? from = null, DateTime? to = null)
        {
            var query = _db.Notifications.Where(n => n.Type == "SystemLog");
            if (from.HasValue) query = query.Where(n => n.CreatedAtUtc >= from.Value);
            if (to.HasValue) query = query.Where(n => n.CreatedAtUtc <= to.Value);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim().ToLowerInvariant();
                query = query.Where(n => n.Message.ToLower().Contains(kw));
            }
            if (!string.IsNullOrWhiteSpace(level))
            {
                var lv = level.Trim().ToUpper();
                query = query.Where(n => n.Message.Contains("\"level\":") && n.Message.ToUpper().Contains(lv));
            }
            var items = await query.OrderByDescending(n => n.CreatedAtUtc).Take(Math.Clamp(take, 50, 1000)).ToListAsync();
            ViewBag.Items = items;
            ViewBag.Q = q;
            ViewBag.Level = level;
            ViewData["Title"] = "System Logs";
            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");
            return View();
        }

        [HttpGet]
        [Route("Logs/Export")]
        public async Task<IActionResult> Export(DateTime? from = null, DateTime? to = null, string? level = null, string? q = null)
        {
            var query = _db.Notifications.Where(n => n.Type == "SystemLog");
            if (from.HasValue) query = query.Where(n => n.CreatedAtUtc >= from.Value);
            if (to.HasValue) query = query.Where(n => n.CreatedAtUtc <= to.Value);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim().ToLowerInvariant();
                query = query.Where(n => n.Message.ToLower().Contains(kw));
            }
            if (!string.IsNullOrWhiteSpace(level))
            {
                var lv = level.Trim().ToUpper();
                query = query.Where(n => n.Message.ToUpper().Contains(lv));
            }
            var list = await query.OrderByDescending(n => n.CreatedAtUtc).ToListAsync();
            var lines = new List<string> { "TimeUTC,Level,Category,Message" };
            foreach (var n in list)
            {
                string lvl = "INFO", cat = "", msg = n.Message;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(n.Message);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("level", out var el)) lvl = el.GetString() ?? lvl;
                    if (root.TryGetProperty("category", out var ce)) cat = ce.GetString() ?? cat;
                    if (root.TryGetProperty("message", out var me)) msg = me.GetString() ?? msg;
                } catch {}
                lines.Add($"{n.CreatedAtUtc:s},{Csv(lvl)},{Csv(cat)},{Csv(msg)}");
            }
            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines));
            return File(bytes, "text/csv", "system-logs.csv");

            static string Csv(string? s)
            {
                var v = s ?? string.Empty;
                return (v.Contains('"')||v.Contains(',')||v.Contains('\n')) ? $"\"{v.Replace("\"","\"\"")}\"" : v;
            }
        }

        // PDF export removed per request; keep CSV export only.
    }
}
