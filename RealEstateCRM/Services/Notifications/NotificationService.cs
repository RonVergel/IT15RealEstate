using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateCRM.Data;
using RealEstateCRM.Models;

namespace RealEstateCRM.Services.Notifications
{
    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public NotificationService(AppDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
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
            return await _db.SaveChangesAsync();
        }

        public async Task<int> NotifyRoleAsync(string roleName, string message, string? linkUrl = null, string? actorUserId = null, string? type = null)
        {
            var users = await _userManager.GetUsersInRoleAsync(roleName);
            return await NotifyUsersAsync(users.Select(u => u.Id), message, linkUrl, actorUserId, type);
        }

        public async Task<int> NotifyAllUsersAsync(string message, string? linkUrl = null, string? actorUserId = null, string? type = null)
        {
            var all = await _userManager.Users.Select(u => u.Id).ToListAsync();
            return await NotifyUsersAsync(all, message, linkUrl, actorUserId, type);
        }
    }
}

