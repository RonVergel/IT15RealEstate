using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;

namespace RealEstateCRM.Controllers
{
    [Authorize]
    [Route("[controller]/[action]")]
    public class NotificationsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public NotificationsController(AppDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Count()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new { count = 0 });
            var count = await _db.Notifications
                .Where(n => n.RecipientUserId == user.Id && !n.IsRead)
                .CountAsync();
            return Json(new { count });
        }

        [HttpGet]
        public async Task<IActionResult> Recent(int take = 10)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(Array.Empty<object>());
            var list = await _db.Notifications
                .Where(n => n.RecipientUserId == user.Id)
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(Math.Clamp(take, 1, 50))
                .Select(n => new
                {
                    n.Id,
                    n.Message,
                    n.LinkUrl,
                    n.IsRead,
                    createdAt = n.CreatedAtUtc
                })
                .ToListAsync();
            return Json(list);
        }

        [HttpPost]
        public async Task<IActionResult> MarkAllRead()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return BadRequest();
            var items = await _db.Notifications
                .Where(n => n.RecipientUserId == user.Id && !n.IsRead)
                .ToListAsync();
            foreach (var n in items) n.IsRead = true;
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> MarkRead(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return BadRequest();
            var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == user.Id);
            if (n == null) return NotFound();
            n.IsRead = true;
            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}

