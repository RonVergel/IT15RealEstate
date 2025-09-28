using System;
using System.ComponentModel.DataAnnotations;

namespace RealEstateCRM.Models
{
    public class Contact
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Agent { get; set; }

        [Required]
        public string Type { get; set; } = "Client";

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime? LastContacted { get; set; }

        // Follow-up related
        public DateTime? NextFollowUpUtc { get; set; }
        public DateTime? FollowUpNotifiedUtc { get; set; }

        // Soft archive timestamp - ensure this is properly declared
        public DateTime? ArchivedAtUtc { get; set; }

        public string? Email { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Occupation { get; set; }
        public decimal? Salary { get; set; }
    }
}
