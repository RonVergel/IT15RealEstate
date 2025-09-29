using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstateCRM.Models;

namespace RealEstateCRM.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class EmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public EmailModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public string Email { get; set; } = string.Empty;

        [TempData]
        public string? StatusMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            [Display(Name = "New email")]
            public string NewEmail { get; set; } = string.Empty;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Unable to load user.");
            Email = await _userManager.GetEmailAsync(user) ?? user.Email ?? string.Empty;
            return Page();
        }

        public async Task<IActionResult> OnPostChangeEmailAsync()
        {
            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Unable to load user.");

            var currentEmail = await _userManager.GetEmailAsync(user);
            if (!string.Equals(currentEmail, Input.NewEmail, StringComparison.OrdinalIgnoreCase))
            {
                var setEmail = await _userManager.SetEmailAsync(user, Input.NewEmail);
                if (!setEmail.Succeeded)
                {
                    StatusMessage = string.Join("; ", setEmail.Errors.Select(e => e.Description));
                    return RedirectToPage();
                }
                // Keep username in sync if it was same as email
                var userName = await _userManager.GetUserNameAsync(user);
                if (string.Equals(userName, currentEmail, StringComparison.OrdinalIgnoreCase))
                {
                    await _userManager.SetUserNameAsync(user, Input.NewEmail);
                }
                await _signInManager.RefreshSignInAsync(user);
                StatusMessage = "Email updated.";
            }
            return RedirectToPage();
        }
    }
}

