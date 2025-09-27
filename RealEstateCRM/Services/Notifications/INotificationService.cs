using RealEstateCRM.Models;

namespace RealEstateCRM.Services.Notifications
{
    public interface INotificationService
    {
        Task<Notification> NotifyUserAsync(string recipientUserId, string message, string? linkUrl = null, string? actorUserId = null, string? type = null);
        Task<int> NotifyUsersAsync(IEnumerable<string> recipientUserIds, string message, string? linkUrl = null, string? actorUserId = null, string? type = null);
        Task<int> NotifyRoleAsync(string roleName, string message, string? linkUrl = null, string? actorUserId = null, string? type = null);
        Task<int> NotifyAllUsersAsync(string message, string? linkUrl = null, string? actorUserId = null, string? type = null);
    }
}

