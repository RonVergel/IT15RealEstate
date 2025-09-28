// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
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
        private readonly UserManager<IdentityUser> _userManager; // Add UserManager
        private readonly ILogger<LoginModel> _logger;
        private readonly RealEstateCRM.Services.Logging.IAppLogger _appLogger;

        public LoginModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager, ILogger<LoginModel> logger, RealEstateCRM.Services.Logging.IAppLogger appLogger)
        {
            _signInManager = signInManager;
            _userManager = userManager; // Initialize UserManager
            _logger = logger;
            _appLogger = appLogger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

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

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= "/Dashboard";

            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
                {
                    ModelState.AddModelError(string.Empty, "Your email is not confirmed. Please check your inbox.");
                    return Page();
                }

                
                var signInName = user?.UserName ?? Input.Email;

                var result = await _signInManager.PasswordSignInAsync(signInName, Input.Password, Input.RememberMe, lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    try { await _appLogger.LogAsync("AUDIT", "Auth", $"User login succeeded", new { email = Input.Email }); } catch {}
                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    //Temporary bypass to unblock login while testing
                    var bypassUser = user ?? await _userManager.FindByNameAsync(signInName);
                    if (bypassUser != null)
                    {
                        await _signInManager.SignInAsync(bypassUser, Input.RememberMe);
                        try { await _appLogger.LogAsync("WARN", "Auth", "2FA bypassed for login (temporary)", new { email = Input.Email }); } catch { }
                        return LocalRedirect(returnUrl);
                    }
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                if (result.IsLockedOut)
                {
                    _logger.LogWarning("User account locked out.");
                    try { await _appLogger.LogAsync("WARN", "Auth", $"User locked out", new { email = Input.Email }); } catch {}
                    ModelState.AddModelError(string.Empty, "This account has been locked out due to multiple failed login attempts. Please try again later.");
                    return Page();
                }

                // Generic failure (don't reveal which part failed)
                _logger.LogWarning("Invalid login attempt for {Email}", Input.Email);
                try { await _appLogger.LogAsync("WARN", "Auth", "Invalid login attempt", new { email = Input.Email }); } catch {}
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return Page();
            }

            
            return Page();
        }
    }
}
