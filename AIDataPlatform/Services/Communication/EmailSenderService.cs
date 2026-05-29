using AIDataPlatform.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace AIDataPlatform.Services.Communication
{
    public class EmailSenderService : IEmailSender<ApplicationUser>
    {
        private readonly string sendGridApiKey;

        public EmailSenderService(IConfiguration configuration)
        {
            sendGridApiKey = configuration.GetSection("SendGrid:ApiKey").Value;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string message)
        {
            await Execute(sendGridApiKey, subject, message, toEmail);
        }

        public async Task Execute(string apiKey, string subject, string message, string toEmail)
        {
            var client = new SendGridClient(apiKey);
            var msg = new SendGridMessage()
            {
                From = new EmailAddress("authentication@yourdomain.ai", "Authentication"),
                Subject = subject,
                PlainTextContent = message,
                HtmlContent = message
            };
            msg.AddTo(new EmailAddress(toEmail));

            // Disable click tracking.
            // See https://sendgrid.com/docs/User_Guide/Settings/tracking.html
            msg.SetClickTracking(false, false);
            var response = await client.SendEmailAsync(msg);
        }

        public async Task SendConfirmationLinkAsync(ApplicationUser user, string email,
        string confirmationLink) => await SendEmailAsync(email, "Confirm your email",
        "Please confirm your account by " +
        $"<a href='{confirmationLink}'>clicking here</a>.");

        public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email,
        string resetLink) => await SendEmailAsync(email, "Reset your password",
        $"Please reset your password by <a href='{resetLink}'>clicking here</a>.");

        public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email,
            string resetCode) => await SendEmailAsync(email, "Reset your password",
            $"Please reset your password using the following code: {resetCode}");

        // User invitation mail wrapper
        public async Task SendInvitationEmailAsync(string email, string invitationLink)
        {
            var subject = "You're invited to join our platform";
            var message = $"You have been invited to join our platform. Please accept the invitation by <a href='{invitationLink}'>clicking here</a>.";

            await SendEmailAsync(email, subject, message);
        }
    }
}