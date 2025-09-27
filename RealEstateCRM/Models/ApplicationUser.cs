using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace RealEstateCRM.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [Display(Name = "Full Name")]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
        public string Name { get; set; } = string.Empty;
    }
}