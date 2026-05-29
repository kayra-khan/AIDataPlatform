using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Net.Mail;
using System.Net;

namespace AIDataPlatform.Plugins
{
    public class EmailPlugin
    {
        private readonly IConfiguration _configuration;

        public EmailPlugin(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [KernelFunction]
        [Description("Sends an email to a recipient.")]
        public async Task SendEmailAsync(
            Kernel kernel,
            [Description("Semicolon delimitated list of emails of the recipients")] string recipientEmails,
            string subject,
            string body
        )
        {
            // Email settings are read from configuration (e.g. appsettings) — never hardcode credentials.
            var emailSettings = _configuration.GetSection("Email");

            var fromAddress = new MailAddress(emailSettings["FromAddress"]);
            var toAddress = new MailAddress(recipientEmails);
            string fromPassword = emailSettings["Password"];

            var smtp = new SmtpClient
            {
                Host = emailSettings["Host"],
                Port = emailSettings.GetValue<int>("Port"),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
            };
            using (var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body
            })
            {
                smtp.Send(message);
            }
            Console.WriteLine("Email sent!");
        }
    }
}
