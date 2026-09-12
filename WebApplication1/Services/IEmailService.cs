namespace WebApplication1.Services;

public interface IEmailService
{
    // Never throws: implementations log and swallow delivery failures so a down mail server
    // can't roll back or block the operation that triggered the email (see SmtpEmailService).
    Task SendAsync(string toAddress, string subject, string body, bool isBodyHtml = false);
}
