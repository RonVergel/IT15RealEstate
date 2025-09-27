namespace RealEstateCRM.Models
{
    public class Offer
    {
        public int Id { get; set; }
        public int DealId { get; set; }
        public Deal? Deal { get; set; }

        public decimal Amount { get; set; }
        public string Status { get; set; } = "Proposed"; // Proposed | Counter | Accepted | Declined | Withdrawn

        public string? FinancingType { get; set; }
        public decimal? EarnestMoney { get; set; }
        public DateTime? CloseDate { get; set; }
        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}

