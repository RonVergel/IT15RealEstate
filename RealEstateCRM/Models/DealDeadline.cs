namespace RealEstateCRM.Models
{
    public class DealDeadline
    {
        public int Id { get; set; }
        public int DealId { get; set; }
        public Deal? Deal { get; set; }

        public string Type { get; set; } = string.Empty; // Inspection, Appraisal, LoanCommitment, TitleClear, Closing, etc.
        public DateTime DueDate { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
        public string? Notes { get; set; }
    }
}

