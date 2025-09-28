using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RealEstateCRM.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class EnableAuthenticatorModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;

        public EnableAuthenticatorModel(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string SharedKey { get; set; } = string.Empty;
        public string AuthenticatorUri { get; set; } = string.Empty;
        public string? QrCodeDataUrl { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "Verification Code")]
            public string VerificationCode { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadSharedKeyAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("Unable to load user");

            if (!ModelState.IsValid)
            {
                await LoadSharedKeyAsync();
                return Page();
            }

            var code = (Input.VerificationCode ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);
            if (!isValid)
            {
                ModelState.AddModelError(string.Empty, "Invalid verification code");
                await LoadSharedKeyAsync();
                return Page();
            }

            await _userManager.SetTwoFactorEnabledAsync(user, true);
            return RedirectToPage("./TwoFactorAuthentication");
        }

        private async Task LoadSharedKeyAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) throw new InvalidOperationException("Unable to load user");

            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrWhiteSpace(key))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                key = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            SharedKey = FormatKey(key ?? string.Empty);
            var email = user.Email ?? user.UserName ?? "user";
            AuthenticatorUri = GenerateQrUri(email, key ?? string.Empty);

            try
            {
                // Generate QR code image as data URI
                QrCodeDataUrl = GenerateQrDataUrl(AuthenticatorUri);
            }
            catch { QrCodeDataUrl = null; }
        }

        private static string FormatKey(string unformattedKey)
        {
            var result = new StringBuilder();
            int currentPosition = 0;
            while (currentPosition + 4 < unformattedKey.Length)
            {
                result.Append(unformattedKey.Substring(currentPosition, 4)).Append(' ');
                currentPosition += 4;
            }
            if (currentPosition < unformattedKey.Length)
            {
                result.Append(unformattedKey.Substring(currentPosition));
            }
            return result.ToString().ToLowerInvariant();
        }

        private static string GenerateQrUri(string email, string secret)
        {
            return $"otpauth://totp/Homey:{Uri.EscapeDataString(email)}?secret={secret}&issuer=Homey&digits=6";
        }

        private static string GenerateQrDataUrl(string payload)
        {
            // Uses QRCoder to create a compact PNG QR
            using var qrGenerator = new QRCoder.QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.Q);
            var png = new QRCoder.PngByteQRCode(qrData);
            var bytes = png.GetGraphic(4);
            return "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
    }
}
