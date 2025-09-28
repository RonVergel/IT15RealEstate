using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
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

        [BindProperty(SupportsGet = true)]
        public bool RememberMe { get; set; }
        public string ReturnUrl { get; set; } = "/Dashboard";

        [TempData] public string? Dev2FACode { get; set; }  // ADD THIS LINE
        [TempData] public string? InfoMessage { get; set; }
        [TempData] public string? ErrorMessage { get; set; }

        // New: flags for UI
        public bool UseAuthenticator { get; set; }
        public bool UseEmail { get; set; }

        private bool DevShowCodeEnabled =>
            _env.IsDevelopment() &&
            _config.GetValue<bool?>("TwoFactor:ShowDevCode") == true;

        public class InputModel
        {
            [Required]
            [DataType(DataType.Text)]
            [Display(Name = "Verification code")]
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
                UseAuthenticator = providers.Contains("Authenticator");
                UseEmail = providers.Contains("Email");

                // For dev/debug: show the current token for whichever provider is available
                if (DevShowCodeEnabled)
                {
                    try
                    {
                        if (UseAuthenticator)
                        {
                            Dev2FACode = await _userManager.GenerateTwoFactorTokenAsync(user, "Authenticator");
                        }
                        else if (UseEmail)
                        {
                            var code = await _userManager.GenerateTwoFactorTokenAsync(user, "Email");
                            Dev2FACode = new string((code ?? "").Where(char.IsLetterOrDigit).ToArray());
                        }
                    }
                    catch { /* swallow dev-only failures */ }
                }

                await _appLogger.LogAsync("INFO", "Auth", "2FA page load", new
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
                _logger.LogWarning("2FA POST: no two-factor user in context. Redirecting to Login.");
                return RedirectToPage("./Login");
            }

            var serverTimeUtc = DateTime.UtcNow;
            _logger.LogWarning("2FA POST: Server time is {ServerTimeUtc} (UTC). Compare this with your authenticator device's UTC time.", serverTimeUtc);

            var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
            var hasAuthenticator = providers.Contains("Authenticator");
            var hasEmail = providers.Contains("Email");

            _logger.LogInformation("2FA POST: userId={UserId} providers={Providers} hasAuth={HasAuth} hasEmail={HasEmail}",
                user.Id, string.Join(",", providers), hasAuthenticator, hasEmail);

            if (!hasAuthenticator && !hasEmail)
            {
                ModelState.AddModelError(string.Empty, "No two-factor provider is available for this account.");
                await SafeLogAsync("ERROR", "No 2FA provider", user.Id, new { providers });
                return Page();
            }

            var authenticatorCode = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);
            var normalized = new string((Input.TwoFactorCode ?? "").Where(char.IsLetterOrDigit).ToArray());
            if (normalized.Length is < 4 or > 10)
            {
                ModelState.AddModelError(string.Empty, "Invalid code format.");
                await SafeLogAsync("WARN", "Bad 2FA code format", user.Id, new { normalizedLength = normalized.Length });
                return Page();
            }

            var providerToUse = hasAuthenticator ? "Authenticator" : "Email";
            var codeToUse = providerToUse == "Authenticator" ? authenticatorCode : normalized;
            _logger.LogInformation("2FA POST: attempting TwoFactorSignInAsync(provider={Provider}, userId={UserId})", providerToUse, user.Id);

            var result = await _signInManager.TwoFactorSignInAsync(providerToUse, codeToUse, RememberMe, Input.RememberMachine);

            _logger.LogInformation("2FA POST: TwoFactorSignInAsync result for userId={UserId} succeeded={Succeeded} lockedOut={LockedOut} isPersistent={IsPersistent}", user.Id, result.Succeeded, result.IsLockedOut, RememberMe);

            if (!result.Succeeded && !result.IsLockedOut)
            {
                // Try the alternate provider if available (covers the case where user pasted the email code while Authenticator is preferred)
                var altProvider = providerToUse == "Authenticator" ? (hasEmail ? "Email" : null) : (hasAuthenticator ? "Authenticator" : null);
                if (!string.IsNullOrEmpty(altProvider))
                {
                    var altCode = altProvider == "Authenticator" ? authenticatorCode : normalized;
                    _logger.LogInformation("2FA POST: trying alternate provider {Alt} for userId={UserId}", altProvider, user.Id);
                    var altResult = await _signInManager.TwoFactorSignInAsync(altProvider, altCode, RememberMe, Input.RememberMachine);
                    _logger.LogInformation("2FA POST: alternate provider result for userId={UserId} provider={Provider} succeeded={Succeeded} lockedOut={LockedOut}", user.Id, altProvider, altResult.Succeeded, altResult.IsLockedOut);

                    if (altResult.Succeeded)
                    {
                        await SafeLogAsync("AUDIT", $"{altProvider} 2FA success", user.Id);
                        return LocalRedirect(returnUrl);
                    }
                    if (altResult.IsLockedOut)
                    {
                        ModelState.AddModelError(string.Empty, "This account is locked out.");
                        await SafeLogAsync("WARN", $"{altProvider} 2FA locked out", user.Id);
                        return Page();
                    }
                    // fall through to manual verification attempts
                }
            }

            if (result.Succeeded)
            {
                await SafeLogAsync("AUDIT", $"{providerToUse} 2FA success", user.Id);
                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "This account is locked out.");
                await SafeLogAsync("WARN", $"{providerToUse} 2FA locked out", user.Id);
                return Page();
            }

            // Manual verification: try both providers as last resort
            bool manualValid = false;
            try
            {
                // Try the provider that was preferred, then the other one if available
                if (hasAuthenticator)
                {
                    manualValid = await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, authenticatorCode);
                    if (!manualValid && hasEmail)
                    {
                        manualValid = await _userManager.VerifyTwoFactorTokenAsync(user, "Email", normalized);
                    }
                }
                else if (hasEmail)
                {
                    manualValid = await _userManager.VerifyTwoFactorTokenAsync(user, "Email", normalized);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "2FA POST: manual VerifyTwoFactorTokenAsync threw for userId={UserId}", user.Id);
                manualValid = false;
            }

            _logger.LogInformation("2FA POST: manual verification for userId={UserId} manualValid={ManualValid}", user.Id, manualValid);

            if (manualValid)
            {
                await _signInManager.SignInAsync(user, RememberMe);
                await SafeLogAsync("AUDIT", $"2FA manual fallback success", user.Id);
                return LocalRedirect(returnUrl);
            }

            ModelState.AddModelError(string.Empty, "Invalid verification code.");
            await SafeLogAsync("WARN", $"2FA invalid code", user.Id, new { codeLen = normalized.Length });
            return Page();
        }

        public async Task<IActionResult> OnPostResendAsync(string? returnUrl = null)
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
                    var safeCode = new string((code ?? "").Where(char.IsLetterOrDigit).ToArray());
                    await _emailSender.SendEmailAsync(
                        user.Email!,
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