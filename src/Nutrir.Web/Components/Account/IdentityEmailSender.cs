using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;

namespace Nutrir.Web.Components.Account;

/// <summary>
/// Real email sender for ASP.NET Identity operations (confirmation, password reset).
/// Delegates to <see cref="IEmailService"/> for actual SMTP delivery.
/// Falls back gracefully when SMTP is not configured.
/// </summary>
internal sealed class IdentityEmailSender : IEmailSender<ApplicationUser>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<IdentityEmailSender> _logger;

    public IdentityEmailSender(IEmailService emailService, ILogger<IdentityEmailSender> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var encodedLink = HtmlEncoder.Default.Encode(confirmationLink);
        var subject = "Confirm your email";
        var htmlBody = BuildEmailHtml(
            subject,
            "Please confirm your email address by clicking the button below.",
            encodedLink,
            "Confirm Email");

        await SendAsync(email, subject, htmlBody);
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var encodedLink = HtmlEncoder.Default.Encode(resetLink);
        var subject = "Reset your password";
        var htmlBody = BuildEmailHtml(
            subject,
            "We received a request to reset your password. Click the button below to choose a new password.",
            encodedLink,
            "Reset Password");

        await SendAsync(email, subject, htmlBody);
    }

    public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var subject = "Reset your password";
        var htmlBody = BuildCodeEmailHtml(
            subject,
            "We received a request to reset your password. Use the code below to proceed.",
            resetCode);

        await SendAsync(email, subject, htmlBody);
    }

    private async Task SendAsync(string email, string subject, string htmlBody)
    {
        if (!_emailService.IsConfigured)
        {
            _logger.LogWarning(
                "SMTP is not configured. Identity email with subject \"{Subject}\" to {Recipient} was not sent",
                subject, email);
            return;
        }

        try
        {
            await _emailService.SendEmailAsync(email, subject, htmlBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send identity email with subject \"{Subject}\" to {Recipient}",
                subject, email);
        }
    }

    private static string BuildEmailHtml(string title, string message, string actionUrl, string buttonText)
    {
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8" /></head>
            <body style="margin:0;padding:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;background-color:#f4f4f7;">
              <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 20px;">
                <tr>
                  <td align="center">
                    <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:8px;padding:40px;">
                      <tr>
                        <td style="text-align:center;padding-bottom:24px;">
                          <h1 style="margin:0;font-size:22px;color:#333333;">{HtmlEncoder.Default.Encode(title)}</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size:15px;line-height:1.6;color:#555555;padding-bottom:32px;">
                          {HtmlEncoder.Default.Encode(message)}
                        </td>
                      </tr>
                      <tr>
                        <td align="center" style="padding-bottom:32px;">
                          <a href="{actionUrl}" style="display:inline-block;padding:12px 32px;background-color:#2563eb;color:#ffffff;text-decoration:none;border-radius:6px;font-size:15px;font-weight:600;">{HtmlEncoder.Default.Encode(buttonText)}</a>
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size:13px;color:#999999;border-top:1px solid #eeeeee;padding-top:20px;">
                          If you did not request this, you can safely ignore this email.
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string BuildCodeEmailHtml(string title, string message, string code)
    {
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8" /></head>
            <body style="margin:0;padding:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;background-color:#f4f4f7;">
              <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 20px;">
                <tr>
                  <td align="center">
                    <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:8px;padding:40px;">
                      <tr>
                        <td style="text-align:center;padding-bottom:24px;">
                          <h1 style="margin:0;font-size:22px;color:#333333;">{HtmlEncoder.Default.Encode(title)}</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size:15px;line-height:1.6;color:#555555;padding-bottom:24px;">
                          {HtmlEncoder.Default.Encode(message)}
                        </td>
                      </tr>
                      <tr>
                        <td align="center" style="padding-bottom:32px;">
                          <div style="display:inline-block;padding:16px 32px;background-color:#f0f0f0;border-radius:8px;font-size:28px;font-weight:700;letter-spacing:4px;color:#333333;">{HtmlEncoder.Default.Encode(code)}</div>
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size:13px;color:#999999;border-top:1px solid #eeeeee;padding-top:20px;">
                          If you did not request this, you can safely ignore this email.
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
