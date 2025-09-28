// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace RealEstateCRM.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly RealEstateCRM.Services.Logging.IAppLogger _appLogger;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public LoginModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            ILogger<LoginModel> logger,
            RealEstateCRM.Services.Logging.IAppLogger appLogger,
            IEmailSender emailSender,
            IConfiguration config,
            IWebHostEnvironment env)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _appLogger = appLogger;
            _emailSender = emailSender;
            _config = config;
            _env = env;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData] public string ErrorMessage { get; set; }
        [TempData] public string Dev2FACode { get; set; }

        // Toggle (true enables automatic bypass of 2FA). Set in appsettings.Development.json:
        // "TwoFactor": { "Bypass": true, "ShowDevCode": true }
        // For production, set Bypass to false.
        private bool Bypass2FAEnabled =>
            _config.GetValue<bool?>("TwoFactor:Bypass") == true
            || (_env.IsDevelopment() && _config.GetValue<bool?>("TwoFactor:Bypass") is null); // default allow in Dev if not explicitly disabled

        private bool DevShowCodeEnabled =>
            _env.IsDevelopment() &&
            _config.GetValue<bool?>("TwoFactor:ShowDevCode") == true;

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= "/Dashboard";

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
            {
                ModelState.AddModelError(string.Empty, "Your email is not confirmed. Please check your inbox.");
                return Page();
            }

            var signInName = user?.UserName ?? Input.Email;

            var result = await _signInManager.PasswordSignInAsync(
                signInName,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in.");
                try { await _appLogger.LogAsync("AUDIT", "Auth", "User login succeeded", new { email = Input.Email }); } catch { }
                return LocalRedirect(returnUrl);
            }

            if (result.RequiresTwoFactor)
            {
                var twoFactorUser = await _signInManager.GetTwoFactorAuthenticationUserAsync() ?? user;

                // ===== ACTIVE 2FA BYPASS =====
                // You requested the bypass be brought back. This block signs the user in immediately
                // when 2FA is required. Disable by setting TwoFactor:Bypass = false in configuration.
                if (Bypass2FAEnabled && twoFactorUser != null)
                {
                    await _signInManager.SignInAsync(twoFactorUser, Input.RememberMe);
                    try
                    {
                        await _appLogger.LogAsync("WARN", "Auth", "2FA bypassed", new
                        {
                            userId = twoFactorUser.Id,
                            email = Input.Email,
                            remember = Input.RememberMe,
                            env = _env.EnvironmentName
                        });
                    }
                    catch { }
                    return LocalRedirect(returnUrl);
                }
                // ===== END BYPASS =====

                if (twoFactorUser != null)
                {
                    try
                    {
                            if (!await _userManager.GetTwoFactorEnabledAsync(twoFactorUser))
                        {
                            await _userManager.SetTwoFactorEnabledAsync(twoFactorUser, true);
                        }

                        var providers = await _userManager.GetValidTwoFactorProvidersAsync(twoFactorUser);
                        if (!providers.Contains("Email"))
                        {
                            ModelState.AddModelError(string.Empty, "Email two-factor provider not available. Contact support.");
                            await _appLogger.LogAsync("ERROR", "Auth", "Email provider missing for 2FA", new { userId = twoFactorUser.Id, providers });
                            return Page();
                        }

                        var code = await _userManager.GenerateTwoFactorTokenAsync(twoFactorUser, "Email");
                        var safeCode = new string(code.Where(char.IsLetterOrDigit).ToArray());

                        await _emailSender.SendEmailAsync(
                            twoFactorUser.Email,
                            "Your Security Code",
                            $"Your login security code is: <strong style='font-size:18px'>{safeCode}</strong><br/><br/>It expires shortly. If you didn't request this you can ignore it.");

                        if (DevShowCodeEnabled) Dev2FACode = safeCode;

                        try
                        {
                            await _appLogger.LogAsync("INFO", "Auth", "2FA email code generated", new
                            {
                                userId = twoFactorUser.Id,
                                providers = string.Join(",", providers),
                                devShown = DevShowCodeEnabled,
                                bypassActive = Bypass2FAEnabled
                            });
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        try { await _appLogger.LogAsync("ERROR", "Auth", "Failed sending 2FA email code", new { ex.Message }); } catch { }
                    }
                }

                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                try { await _appLogger.LogAsync("WARN", "Auth", "User locked out", new { email = Input.Email }); } catch { }
                ModelState.AddModelError(string.Empty, "This account has been locked out due to multiple failed login attempts. Please try again later.");
                return Page();
            }

            _logger.LogWarning("Invalid login attempt for {Email}", Input.Email);
            try { await _appLogger.LogAsync("WARN", "Auth", "Invalid login attempt", new { email = Input.Email }); } catch { }
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return Page();
        }
    }
}
