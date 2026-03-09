using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;

namespace Nutrir.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly SmtpOptions _options;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<SmtpOptions> options, ILogger<EmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => SendEmailAsync(to, toName: null, subject, htmlBody, ct);

    public Task SendEmailAsync(string to, string? toName, string subject, string htmlBody, CancellationToken ct = default)
        => SendInternalAsync(to, toName, subject, htmlBody, ct);

    private async Task SendInternalAsync(string to, string? toName, string subject, string htmlBody, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to, nameof(to));
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subject));
        ArgumentNullException.ThrowIfNull(htmlBody, nameof(htmlBody));

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.SenderName, _options.SenderEmail));
        message.To.Add(new MailboxAddress(toName, to));
        message.Subject = subject;

        message.Body = new TextPart("html")
        {
            Text = htmlBody
        };

        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_options.Username, _options.Password, ct);
            await client.SendAsync(message, ct);

            _logger.LogInformation("Email sent to {Recipient} with subject \"{Subject}\"", to, subject);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or AuthenticationException)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient} with subject \"{Subject}\"", to, subject);
            throw;
        }
        finally
        {
            await client.DisconnectAsync(quit: true, CancellationToken.None);
        }
    }
}
