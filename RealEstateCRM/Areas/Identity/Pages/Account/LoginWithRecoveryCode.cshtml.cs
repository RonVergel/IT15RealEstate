using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RealEstateCRM.Models;

namespace RealEstateCRM.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWithRecoveryCodeModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RealEstateCRM.Services.Logging.IAppLogger _appLogger;

        public LoginWithRecoveryCodeModel(SignInManager<ApplicationUser> signInManager, RealEstateCRM.Services.Logging.IAppLogger appLogger)
        {
            _signInManager = signInManager;
            _appLogger = appLogger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string ReturnUrl { get; set; } = "/Dashboard";

        public class InputModel
        {
            [Required]
            [DataType(DataType.Text)]
            [Display(Name = "Recovery code")]
            public string RecoveryCode { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                return RedirectToPage("./Login");
            }
            ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/Dashboard" : returnUrl;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            returnUrl ??= "/Dashboard";
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                return RedirectToPage("./Login");
            }

            var code = (Input.RecoveryCode ?? string.Empty).Replace(" ", string.Empty);
            var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(code);
            if (result.Succeeded)
            {
                try { await _appLogger.LogAsync("AUDIT", "Auth", "Recovery code login succeeded", new { userId = user.Id }); } catch { }
                return LocalRedirect(returnUrl);
            }
            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "This account is locked out.");
                return Page();
            }
            try { await _appLogger.LogAsync("WARN", "Auth", "Invalid recovery code", new { userId = user.Id }); } catch { }
            ModelState.AddModelError(string.Empty, "Invalid recovery code.");
            return Page();
        }
    }
}

