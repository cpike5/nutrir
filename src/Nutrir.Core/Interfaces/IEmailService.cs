namespace Nutrir.Core.Interfaces;

public interface IEmailService
{
    bool IsConfigured { get; }
    Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    Task SendEmailAsync(string to, string? toName, string subject, string htmlBody, CancellationToken ct = default);
    Task SendEmailWithAttachmentAsync(string to, string? toName, string subject, string htmlBody,
        byte[] attachmentBytes, string attachmentFileName, string attachmentContentType,
        CancellationToken ct = default);
}
