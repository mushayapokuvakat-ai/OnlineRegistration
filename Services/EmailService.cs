using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OnlineRegistrationSystem.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string message)
        {
            var smtpServer = _config["EmailSettings:SmtpServer"];
            var port = int.Parse(_config["EmailSettings:Port"] ?? "587");
            var senderEmail = _config["EmailSettings:SenderEmail"];
            var senderPassword = _config["EmailSettings:SenderPassword"];

            // If credentials are placeholder, just log it
            if (string.IsNullOrEmpty(senderPassword) || senderPassword == "your-app-password")
            {
                _logger.LogWarning($"[EMAIL SIMULATION] To: {toEmail}, Subject: {subject}, Body: {message}");
                return;
            }

            try
            {
                using (var client = new SmtpClient(smtpServer, port))
                {
                    client.EnableSsl = true;
                    client.Credentials = new NetworkCredential(senderEmail, senderPassword);

                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(senderEmail),
                        Subject = subject,
                        Body = message,
                        IsBodyHtml = true
                    };
                    mailMessage.To.Add(toEmail);

                    await client.SendMailAsync(mailMessage);
                    _logger.LogInformation($"Email sent to {toEmail}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send email to {toEmail}");
            }
        }

        public async Task SendVerificationEmailAsync(string toEmail, string name)
        {
            var subject = "Welcome to CourseReg Academy!";
            var message = $"<h1>Welcome, {name}!</h1><p>Thank you for registering. Please login to enroll in courses.</p>";
            await SendEmailAsync(toEmail, subject, message);
        }
    }
}
