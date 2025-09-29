using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstateCRM.Models;

namespace RealEstateCRM.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class TwoFactorAuthenticationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public TwoFactorAuthenticationModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public bool Is2faEnabled { get; set; }
        public bool HasAuthenticator { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Unable to load user.");

            Is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            HasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) != null;
            return Page();
        }
    }
}

