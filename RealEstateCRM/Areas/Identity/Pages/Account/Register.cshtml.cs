// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection; // for ActivatorUtilitiesConstructor
using RealEstateCRM.Models;

namespace RealEstateCRM.Areas.Identity.Pages.Account
{
    // Restrict registration page to admins (Broker role) only
    [Authorize(Roles = "Broker")]
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;

        // Mark the intended constructor explicitly to avoid DI ambiguity
        [ActivatorUtilitiesConstructor]
        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
            _emailStore = GetEmailStore(); // _userManager is already set
            _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "Username")]
            public string UserName { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            // Only Brokers can reach this page because of the class-level Authorize attribute.
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            // Admins create agent accounts — default redirect back to Dashboard
            returnUrl ??= "/Dashboard";
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                var user = CreateUser();

                // Use the provided Username and Email for the Identity user
                await _userStore.SetUserNameAsync(user, Input.UserName, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("Admin created a new user account.");

                    // Ensure the "Agent" role exists and add the new user to it
                    if (!await _roleManager.RoleExistsAsync("Agent"))
                    {
                        await _roleManager.CreateAsync(new IdentityRole("Agent"));
                    }

                    var addToRoleResult = await _userManager.AddToRoleAsync(user, "Agent");
                    if (!addToRoleResult.Succeeded)
                    {
                        // Rollback user creation if role assignment fails (optional)
                        await _userManager.DeleteAsync(user);
                        foreach (var err in addToRoleResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, err.Description);
                        }
                        return Page();
                    }

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                        protocol: Request.Scheme);

                    // Email body
                    var emailBody = $@"
                        <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                            <div style='max-width: 600px; margin: 20px auto; padding: 20px; border: 1px solid #ddd; border-radius: 10px;'>
                                <h2 style='text-align: center; color: #0056b3;'>Welcome to Real Estate CRM!</h2>
                                <p>An admin has created an account for you. Please confirm your email address to activate the account.</p>
                                <div style='text-align: center; margin: 30px 0;'>
                                    <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' style='background-color: #0056b3; color: white; padding: 15px 25px; text-decoration: none; border-radius: 5px; font-size: 16px;'>Confirm Your Account</a>
                                </div>
                                <p>If you are having trouble with the button above, please copy and paste the URL below into your web browser:</p>
                                <p style='word-break: break-all; font-size: 12px;'><a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>{HtmlEncoder.Default.Encode(callbackUrl)}</a></p>
                                <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'/>
                                <p style='font-size: 12px; color: #888;'>If you did not expect this account, please contact your administrator.</p>
                            </div>
                        </div>";

                    await _emailSender.SendEmailAsync(Input.Email, "Confirm your email for Real Estate CRM", emailBody);

                    // Do NOT sign in the created user. Admin created the account.
                    TempData["SuccessMessage"] = "Agent account created. A confirmation email was sent to the new user.";

                    return LocalRedirect(returnUrl);
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
