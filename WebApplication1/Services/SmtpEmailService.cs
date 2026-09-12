using System.Net;
using System.Net.Mail;

namespace WebApplication1.Services;

// Minimal SMTP sender built on the framework's own System.Net.Mail — no email infrastructure
// existed in this project before, so this is the smallest addition that covers it. Configured
// entirely from the "Smtp" section in appsettings (see appsettings.json.example); if Host is
// left blank, sending is a logged no-op rather than a startup failure, so the app keeps working
// without mail server credentials configured.
public class SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger) : IEmailService
{
    public async Task SendAsync(string toAddress, string subject, string body)
    {
        var section = configuration.GetSection("Smtp");
        var host = section["Host"];

        if (string.IsNullOrWhiteSpace(host))
        {
            logger.LogInformation("Smtp:Host is not configured; skipping email '{Subject}' to {ToAddress}.", subject, toAddress);
            return;
        }

        try
        {
            using var client = new SmtpClient(host, int.TryParse(section["Port"], out var port) ? port : 587)
            {
                EnableSsl = !bool.TryParse(section["EnableSsl"], out var enableSsl) || enableSsl
            };

            var user = section["User"];
            var password = section["Password"];
            if (!string.IsNullOrEmpty(user))
            {
                client.Credentials = new NetworkCredential(user, password);
            }

            var fromAddress = section["FromAddress"] ?? user ?? "noreply@localhost";
            using var message = new MailMessage(fromAddress, toAddress, subject, body);
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email '{Subject}' to {ToAddress}.", subject, toAddress);
        }
    }
}
