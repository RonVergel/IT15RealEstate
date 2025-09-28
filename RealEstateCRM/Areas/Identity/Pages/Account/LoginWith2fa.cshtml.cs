using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RealEstateCRM.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RealEstateCRM.Services.Logging.IAppLogger _appLogger;

        public LoginWith2faModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager, RealEstateCRM.Services.Logging.IAppLogger appLogger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _appLogger = appLogger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public bool RememberMe { get; set; }

        public string ReturnUrl { get; set; } = "/Dashboard";

        public class InputModel
        {
            [Required]
            [DataType(DataType.Text)]
            [Display(Name = "Authenticator code")]
            public string TwoFactorCode { get; set; } = string.Empty;

            [Display(Name = "Remember this machine")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe, string? returnUrl = null)
        {
            // Ensure the user has gone through the username & password screen first
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                return RedirectToPage("./Login");
            }

            RememberMe = rememberMe;
            ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/Dashboard" : returnUrl;
            try
            {
                var twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
                var hasKey = await _userManager.GetAuthenticatorKeyAsync(user) != null;
                await _appLogger.LogAsync("INFO", "Auth", "2FA page loaded", new { userId = user.Id, rememberMe = RememberMe, twoFactorEnabled, hasKey, returnUrl = ReturnUrl });
            }
            catch { }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(bool rememberMe, string? returnUrl = null)
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

            var authenticatorCode = new string((Input.TwoFactorCode ?? string.Empty).Where(char.IsDigit).ToArray());

            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, Input.RememberMachine);

            if (result.Succeeded)
            {
                try { await _appLogger.LogAsync("AUDIT", "Auth", "2FA login succeeded", new { userId = user.Id }); } catch { }
                return LocalRedirect(returnUrl);
            }
            else if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "This account is locked out.");
                try { await _appLogger.LogAsync("WARN", "Auth", "2FA locked out", new { userId = user.Id }); } catch { }
                return Page();
            }
            // As a safety net: if verification is valid but SignIn failed, complete sign-in manually
            var valid = await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, authenticatorCode);
            if (valid)
            {
                await _signInManager.SignInAsync(user, rememberMe);
                try { await _appLogger.LogAsync("AUDIT", "Auth", "2FA token valid, manual sign-in", new { userId = user.Id }); } catch { }
                return LocalRedirect(returnUrl);
            }
            try
            {
                var twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
                var hasKey = await _userManager.GetAuthenticatorKeyAsync(user) != null;
                await _appLogger.LogAsync("WARN", "Auth", "Invalid 2FA code attempt", new { userId = user.Id, twoFactorEnabled, hasKey });
            }
            catch { }
            ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
            return Page();
        }
    }
}
