using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;

namespace RealEstateCRM.Services.Notifications
{
    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RealEstateCRM.Services.Logging.IAppLogger _appLogger;

        public NotificationService(AppDbContext db, UserManager<ApplicationUser> userManager, RealEstateCRM.Services.Logging.IAppLogger appLogger)
        {
            _db = db;
            _userManager = userManager;
            _appLogger = appLogger;
        }

        public async Task<Notification> NotifyUserAsync(string recipientUserId, string message, string? linkUrl = null, string? actorUserId = null, string? type = null)
        {
            var notif = new Notification
            {
                RecipientUserId = recipientUserId,
                ActorUserId = actorUserId,
                Message = message,
                LinkUrl = linkUrl,
                Type = type,
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false
            };
            _db.Notifications.Add(notif);
            await _db.SaveChangesAsync();
            try
            {
                var category = string.IsNullOrWhiteSpace(type) ? "Notification" : type;
                await _appLogger.LogAsync("AUDIT", category, message, new { recipientUserId, linkUrl }, actorUserId);
            }
            catch { }
            return notif;
        }

        public async Task<int> NotifyUsersAsync(IEnumerable<string> recipientUserIds, string message, string? linkUrl = null, string? actorUserId = null, string? type = null)
        {
            var now = DateTime.UtcNow;
            var list = recipientUserIds.Distinct().Select(uid => new Notification
            {
                RecipientUserId = uid,
                ActorUserId = actorUserId,
                Message = message,
                LinkUrl = linkUrl,
                Type = type,
                CreatedAtUtc = now,
                IsRead = false
            }).ToList();
            if (list.Count == 0) return 0;
            _db.Notifications.AddRange(list);
            var count = await _db.SaveChangesAsync();
            try
            {
                var category = string.IsNullOrWhiteSpace(type) ? "Notification" : type;
                await _appLogger.LogAsync("AUDIT", category, message, new { recipients = list.Select(x => x.RecipientUserId).ToArray(), linkUrl }, actorUserId);
            }
            catch { }
            return count;
        }

        public async Task<int> NotifyRoleAsync(string roleName, string message, string? linkUrl = null, string? actorUserId = null, string? type = null)
        {
            var users = await _userManager.GetUsersInRoleAsync(roleName);
            var result = await NotifyUsersAsync(users.Select(u => u.Id), message, linkUrl, actorUserId, type);
            try
            {
                var category = string.IsNullOrWhiteSpace(type) ? "Notification" : type;
                await _appLogger.LogAsync("AUDIT", category, message, new { roleName, recipientsCount = users.Count, linkUrl }, actorUserId);
            }
            catch { }
            return result;
        }

        public async Task<int> NotifyAllUsersAsync(string message, string? linkUrl = null, string? actorUserId = null, string? type = null)
        {
            var all = await _userManager.Users.Select(u => u.Id).ToListAsync();
            var result = await NotifyUsersAsync(all, message, linkUrl, actorUserId, type);
            try
            {
                var category = string.IsNullOrWhiteSpace(type) ? "Notification" : type;
                await _appLogger.LogAsync("AUDIT", category, message, new { broadcast = true, recipientsCount = all.Count, linkUrl }, actorUserId);
            }
            catch { }
            return result;
        }
    }
}
