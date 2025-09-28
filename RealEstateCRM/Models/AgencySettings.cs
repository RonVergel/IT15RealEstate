namespace RealEstateCRM.Models
{
    public class AgencySettings
    {
        public int Id { get; set; }
        // Commission shares in percent (0-100)
        public decimal BrokerCommissionPercent { get; set; } = 10m;
        public decimal AgentCommissionPercent { get; set; } = 5m;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        // Monthly revenue goal (sum of commissions) in currency
        public decimal MonthlyRevenueGoal { get; set; } = 0m;
        // Track notifications to avoid duplicates; format YYYYMM (e.g., 202509)
        public int? LastNotifiedAchievedPeriod { get; set; }
        public int? LastNotifiedBehindPeriod { get; set; }

        // Default deadline offsets (days from UnderContract)
        public int InspectionDays { get; set; } = 7;
        public int AppraisalDays { get; set; } = 14;
        public int LoanCommitmentDays { get; set; } = 21;
        public int ClosingDays { get; set; } = 30;

        // Assignment limits
        public int MaxActiveAssignmentsPerAgent { get; set; } = 5;
        public int MaxDeclinesPerAgentPerMonth { get; set; } = 3;
    }
}
