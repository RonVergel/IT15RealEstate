using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RealEstateCRM.Models
{
    public class Deal
    {
        public int Id { get; set; }
        
        [Required]
        public string Title { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        [ForeignKey("PropertyId")]
        public Property? Property { get; set; }
        
        public string Status { get; set; } = "New";
        
        public string? AgentName { get; set; }
        
        public string? ClientName { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal? OfferAmount { get; set; }
        
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastUpdated { get; set; } = DateTime.UtcNow;
        public DateTime? ClosedAtUtc { get; set; }
        public string? ClosedByUserId { get; set; }
        
        public int DisplayOrder { get; set; } = 0;
    }
}
