using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using WebApplication1.Models.Identity;

namespace WebApplication1.Services;

/// <summary>
/// Self-service "forgot password" for the tenant and platform sign-in pages. Emails a one-hour link; the link carries the Identity
/// reset token (base64url) and the account email. Callers decide WHO may use it (tenant users vs platform staff) - this service
/// only issues and redeems tokens.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>Emails a reset link. <paramref name="buildLink"/> receives the encoded token and returns the absolute link.</summary>
    Task SendLinkAsync(ApplicationUser user, Func<string, string> buildLink);

    /// <summary>Redeems an emailed token. On success also clears lockout and any temporary-password flag.</summary>
    Task<IdentityResult> ResetAsync(ApplicationUser user, string encodedToken, string newPassword);
}

public class PasswordResetService(UserManager<ApplicationUser> userManager, IEmailService emailService) : IPasswordResetService
{
    public const string InvalidTokenMessage = "This reset link is invalid or has expired. Request a new one.";

    public async Task SendLinkAsync(ApplicationUser user, Func<string, string> buildLink)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = buildLink(WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)));

        var body = $"""
            <p>Hello {WebUtility.HtmlEncode(user.FullName)},</p>
            <p>We received a request to reset your password. Click the link below to choose a new one:</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Reset your password</a></p>
            <p>This link expires in 1 hour. If you did not request this, you can ignore this email.</p>
            """;
        await emailService.SendAsync(user.Email!, "Reset your password", body, isBodyHtml: true);
    }

    public async Task<IdentityResult> ResetAsync(ApplicationUser user, string encodedToken, string newPassword)
    {
        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (FormatException)
        {
            return IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = InvalidTokenMessage });
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            return IdentityResult.Failed(result.Errors
                .Select(e => e.Code == "InvalidToken" ? new IdentityError { Code = e.Code, Description = InvalidTokenMessage } : e)
                .ToArray());
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;   // the user just chose their own password
            await userManager.UpdateAsync(user);
        }
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        return result;
    }
}
