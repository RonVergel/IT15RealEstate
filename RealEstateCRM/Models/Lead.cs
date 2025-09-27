using System.ComponentModel.DataAnnotations;

namespace RealEstateCRM.Models
{
    public class Lead
    {
        public int Id { get; set; }
        
        [Required]
        [Display(Name = "Lead Name")]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
        public string Name { get; set; } = string.Empty;
        
        [Display(Name = "Agent")]
        [StringLength(100, ErrorMessage = "Agent name cannot exceed 100 characters")]
        public string? Agent { get; set; }
        
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        [Display(Name = "Email Address")]
        public string? Email { get; set; }
        
        [Required]
        [Phone(ErrorMessage = "Please enter a valid phone number")]
        [Display(Name = "Phone Number")]
        public string Phone { get; set; } = string.Empty;
        
        [Display(Name = "Date Created")]
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        
        [Display(Name = "Last Contacted")]
        public DateTime? LastContacted { get; set; }
        
        [Display(Name = "Notes")]
        [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters")]
        public string? Notes { get; set; }
        
        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;
        
        // Optional: Track if this lead was converted from a contact
        [Display(Name = "Original Contact ID")]
        public int? OriginalContactId { get; set; }
        
        [Display(Name = "Lead Source")]
        [StringLength(50)]
        public string? LeadSource { get; set; } = "Manual"; // "Manual", "Converted", "Website", etc.

        // NEW: Occupation and Salary (match Contact)
        [Display(Name = "Occupation")]
        [StringLength(100, ErrorMessage = "Occupation cannot exceed 100 characters")]
        public string? Occupation { get; set; }

        [Display(Name = "Salary (₱)")]
        [DataType(DataType.Currency)]
        public decimal? Salary { get; set; }
    }
}
