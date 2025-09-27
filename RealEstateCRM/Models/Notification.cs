using System.ComponentModel.DataAnnotations;

namespace RealEstateCRM.Models
{
    public class Notification
    {
        public int Id { get; set; }

        // Recipient user id; when null, treat as for all users in a role or global broadcast
        public string? RecipientUserId { get; set; }

        // Actor who caused the notification (optional)
        public string? ActorUserId { get; set; }

        [Required]
        [MaxLength(512)]
        public string Message { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? LinkUrl { get; set; }

        public bool IsRead { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(64)]
        public string? Type { get; set; }
    }
}

