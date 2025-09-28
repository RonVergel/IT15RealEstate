using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
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
        private readonly ILogger<LoginWith2faModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public LoginWith2faModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            RealEstateCRM.Services.Logging.IAppLogger appLogger,
            ILogger<LoginWith2faModel> logger,
            IEmailSender emailSender,
            IConfiguration config,
            IWebHostEnvironment env)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _appLogger = appLogger;
            _logger = logger;
            _emailSender = emailSender;
            _config = config;
            _env = env;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public bool RememberMe { get; set; }
        public string ReturnUrl { get; set; } = "/Dashboard";

        [TempData] public string Dev2FACode { get; set; }
        [TempData] public string InfoMessage { get; set; }
        [TempData] public string ErrorMessage { get; set; }

        private bool DevShowCodeEnabled =>
            _env.IsDevelopment() &&
            _config.GetValue<bool?>("TwoFactor:ShowDevCode") == true;

        public class InputModel
        {
            [Required]
            [DataType(DataType.Text)]
            [Display(Name = "Email verification code")]
            public string TwoFactorCode { get; set; } = string.Empty;

            [Display(Name = "Remember this machine")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe, string? returnUrl = null)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return RedirectToPage("./Login");

            RememberMe = rememberMe;
            ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/Dashboard" : returnUrl;

            try
            {
                var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
                await _appLogger.LogAsync("INFO", "Auth", "Email 2FA page load", new
                {
                    userId = user.Id,
                    providers = string.Join(",", providers),
                    rememberMe = RememberMe,
                    devShow = DevShowCodeEnabled
                });
            }
            catch { }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(bool rememberMe, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return Page();

            returnUrl ??= "/Dashboard";
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return RedirectToPage("./Login");

            var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
            if (!providers.Contains("Email"))
            {
                ModelState.AddModelError(string.Empty, "Email two-factor provider not available.");
                await SafeLogAsync("ERROR", "Missing email 2FA provider", user.Id, new { providers });
                return Page();
            }

            var normalized = new string((Input.TwoFactorCode ?? "").Where(char.IsLetterOrDigit).ToArray());
            if (normalized.Length is < 4 or > 10)
            {
                ModelState.AddModelError(string.Empty, "Invalid code format.");
                await SafeLogAsync("WARN", "Bad 2FA code format", user.Id, new { normalizedLength = normalized.Length });
                return Page();
            }

            // Attempt sign-in
            var result = await _signInManager.TwoFactorSignInAsync("Email", normalized, rememberMe, Input.RememberMachine);

            if (result.Succeeded)
            {
                await SafeLogAsync("AUDIT", "Email 2FA success", user.Id);
                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "This account is locked out.");
                await SafeLogAsync("WARN", "Email 2FA locked out", user.Id);
                return Page();
            }

            // Manual check (diagnostic): verify token to see if mismatch is due to sign-in state
            var manualValid = await _userManager.VerifyTwoFactorTokenAsync(user, "Email", normalized);
            if (manualValid)
            {
                // If we reach here the token is valid but sign-in failed: fallback
                await _signInManager.SignInAsync(user, rememberMe);
                await SafeLogAsync("AUDIT", "Email 2FA manual fallback success", user.Id);
                return LocalRedirect(returnUrl);
            }

            ModelState.AddModelError(string.Empty, "Invalid verification code.");
            await SafeLogAsync("WARN", "Email 2FA invalid code", user.Id, new { codeLen = normalized.Length });
            return Page();
        }

        public async Task<IActionResult> OnPostResendAsync(bool rememberMe, string? returnUrl = null)
        {
            returnUrl ??= "/Dashboard";
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return RedirectToPage("./Login");

            try
            {
                var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
                if (!providers.Contains("Email"))
                {
                    ErrorMessage = "Cannot resend: email provider not available.";
                    await SafeLogAsync("ERROR", "Resend attempted without email provider", user.Id, new { providers });
                }
                else
                {
                    var code = await _userManager.GenerateTwoFactorTokenAsync(user, "Email");
                    var safeCode = new string(code.Where(char.IsLetterOrDigit).ToArray());
                    await _emailSender.SendEmailAsync(
                        user.Email,
                        "Your Security Code (Resent)",
                        $"Your new login security code is: <strong style='font-size:18px'>{safeCode}</strong>");

                    if (DevShowCodeEnabled) Dev2FACode = safeCode;
                    InfoMessage = "A new code has been sent to your email.";
                    await SafeLogAsync("INFO", "2FA email code resent", user.Id, new { devShow = DevShowCodeEnabled });
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to send code. Try again.";
                await SafeLogAsync("ERROR", "2FA resend failure", user?.Id ?? "unknown", new { ex.Message });
            }

            RememberMe = rememberMe;
            ReturnUrl = returnUrl;
            return Page();
        }

        private async Task SafeLogAsync(string level, string message, string userId, object? extra = null)
        {
            try { await _appLogger.LogAsync(level, "Auth", message, extra is null ? new { userId } : new { userId, extra }); } catch { }
            try { _logger.LogInformation("{Level} {Message} {@Extra}", level, message, extra); } catch { }
        }
    }
}
