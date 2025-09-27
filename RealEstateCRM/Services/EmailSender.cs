using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using Mailjet.Client;
using Mailjet.Client.TransactionalEmails;
using System.Threading.Tasks;
using System.Linq;

namespace RealEstateCRM.Services
{
    // Helper class to hold Mailjet settings from configuration
    public class AuthMessageSenderOptions
    {
        public string? ApiKey { get; set; }
        public string? SecretKey { get; set; }
        public string? SenderEmail { get; set; } // Add SenderEmail
    }

    public class EmailSender : IEmailSender
    {
        private readonly ILogger<EmailSender> _logger;
        public AuthMessageSenderOptions Options { get; }

        public EmailSender(IOptions<AuthMessageSenderOptions> optionsAccessor, ILogger<EmailSender> logger)
        {
            Options = optionsAccessor.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
        {
            if (string.IsNullOrEmpty(Options.ApiKey) || string.IsNullOrEmpty(Options.SecretKey))
            {
                _logger.LogError("Mailjet API Key or Secret Key is not configured.");
                return;
            }

            var client = new MailjetClient(Options.ApiKey, Options.SecretKey);

            // Clean up any literal line break escape sequences that may have been
            // embedded in HTML (e.g., "\r\n"). These show up visibly in some
            // email clients if not removed.
            var cleanedHtml = (htmlMessage ?? string.Empty)
                .Replace("\\r\\n", " ")   // literal backslash-r backslash-n
                .Replace("\r\n", " ");     // actual CRLF to space

            // Use the configured sender email, or a fallback
            var senderEmail = !string.IsNullOrEmpty(Options.SenderEmail) ? Options.SenderEmail : "donotreply@yourdomain.com";

            var email = new TransactionalEmailBuilder()
                .WithFrom(new SendContact(senderEmail, "Real Estate CRM"))
                .WithSubject(subject)
                .WithHtmlPart(cleanedHtml)
                .WithTo(new SendContact(toEmail))
                .Build();

            var response = await client.SendTransactionalEmailAsync(email);

            if (response.Messages != null && response.Messages[0].Status == "success")
            {
                _logger.LogInformation("Email to {Email} queued successfully!", toEmail);
            }
            else
            {
                // Improved error logging
                var error = response.Messages?[0].Errors?.FirstOrDefault();
                _logger.LogError("Failed to send email to {Email}. Status: {Status}. Error: {ErrorMessage}", 
                    toEmail, 
                    response.Messages?[0].Status,
                    error?.ErrorMessage ?? "Unknown error");
            }
        }
    }
}
