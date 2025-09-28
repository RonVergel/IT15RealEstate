using System.Text.Json;
using RealEstateCRM.Data;
using RealEstateCRM.Models;

namespace RealEstateCRM.Services.Logging
{
    public interface IAppLogger
    {
        Task LogAsync(string level, string category, string message, object? context = null, string? actorUserId = null);
    }

    public class AppLogger : IAppLogger
    {
        private readonly AppDbContext _db;
        public AppLogger(AppDbContext db) { _db = db; }

        public async Task LogAsync(string level, string category, string message, object? context = null, string? actorUserId = null)
        {
            var body = JsonSerializer.Serialize(new
            {
                level = level?.ToUpperInvariant() ?? "INFO",
                category = category ?? string.Empty,
                message = message ?? string.Empty,
                context,
                timestampUtc = DateTime.UtcNow
            });
            _db.Notifications.Add(new Notification
            {
                ActorUserId = actorUserId,
                RecipientUserId = null, // system log
                Message = body,
                LinkUrl = null,
                Type = "SystemLog",
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false
            });
            await _db.SaveChangesAsync();
        }
    }
}

