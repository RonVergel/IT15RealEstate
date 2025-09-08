using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RealEstateCRM.Attributes;

namespace RealEstateCRM.Models
{
    public class Property
    {
        public int Id { get; set; }
        
        [Required]
        [Display(Name = "Property Title")]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Address { get; set; } = string.Empty;
        
        [Range(0, double.MaxValue)]
        [Display(Name = "Property Price")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }
        
        [Range(0, int.MaxValue)]
        [Display(Name = "Area (sqm)")]
        public int? Area { get; set; }
        
        [Range(0, double.MaxValue)]
        [Display(Name = "SQFT")]
        public double? SQFT { get; set; }
        
        [Range(0, int.MaxValue)]
        public int? Bedrooms { get; set; }
        
        [Range(0, int.MaxValue)]
        public int? Bathrooms { get; set; }
        
        [Required]
        public string PropertyType { get; set; } = string.Empty; // "Residential", "Commercial", or "Raw Land"
        
        public string? Type { get; set; }
        
        [Display(Name = "Listing Status")]
        public string? ListingStatus { get; set; }
        
        [Display(Name = "Listing Time")]
        public DateTime ListingTime { get; set; } = DateTime.Now;
        
        [Display(Name = "Days on Market")]
        public int? DaysOnMarket { get; set; }
        
        [Display(Name = "Property Link")]
        [Url]
        public string? PropertyLink { get; set; }
        
        [Display(Name = "Price Per SQFT")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal? PricePerSQFT { get; set; }
        
        public string? Agent { get; set; }
        
        public string? Description { get; set; }
        
        [Display(Name = "Property Image")]
        public string? ImagePath { get; set; }
        
        // This property is not mapped to the database - it's just for file upload
        [NotMapped]
        [Display(Name = "Upload Image")]
        public IFormFile? ImageFile { get; set; }

        // Method to calculate SQFT from Area (sqm)
        // 1 sqm = 10.7639 sqft
        public void CalculateSQFT()
        {
            if (Area.HasValue && Area.Value > 0)
            {
                SQFT = Area.Value * 10.7639;
            }
        }

        // Method to calculate Price Per SQFT
        public void CalculatePricePerSQFT()
        {
            if (Price > 0 && SQFT.HasValue && SQFT.Value > 0)
            {
                PricePerSQFT = Price / (decimal)SQFT.Value;
            }
        }
    }
}