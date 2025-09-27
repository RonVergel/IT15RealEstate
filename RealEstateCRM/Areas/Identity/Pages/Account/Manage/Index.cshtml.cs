using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RealEstateCRM.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public IndexModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public string Username { get; set; } = string.Empty;

        [TempData]
        public string? StatusMessage { get; set; }

        public class InputModel
        {
            [Display(Name = "Full name")]
            [StringLength(100)]
            public string? FullName { get; set; }

            [Phone]
            [Display(Name = "Phone number")]
            public string? PhoneNumber { get; set; }
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Unable to load user.");

            Username = await _userManager.GetUserNameAsync(user) ?? user.Email ?? user.Id;
            Input.PhoneNumber = await _userManager.GetPhoneNumberAsync(user);

            // Load FullName from claim if present
            try
            {
                var claims = await _userManager.GetClaimsAsync(user);
                Input.FullName = claims.FirstOrDefault(c => c.Type == "FullName")?.Value;
            }
            catch { }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Unable to load user.");

            // Update phone
            var currentPhone = await _userManager.GetPhoneNumberAsync(user);
            if (Input.PhoneNumber != currentPhone)
            {
                var res = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
                if (!res.Succeeded)
                {
                    StatusMessage = "Unexpected error when setting phone number.";
                    return RedirectToPage();
                }
            }

            // Update FullName claim
            try
            {
                var claims = await _userManager.GetClaimsAsync(user);
                var existing = claims.FirstOrDefault(c => c.Type == "FullName");
                if (existing != null && existing.Value != (Input.FullName ?? string.Empty))
                {
                    await _userManager.ReplaceClaimAsync(user, existing, new Claim("FullName", Input.FullName ?? string.Empty));
                }
                else if (existing == null && !string.IsNullOrWhiteSpace(Input.FullName))
                {
                    await _userManager.AddClaimAsync(user, new Claim("FullName", Input.FullName));
                }
            }
            catch { }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Your profile has been updated";
            return RedirectToPage();
        }
    }
}

