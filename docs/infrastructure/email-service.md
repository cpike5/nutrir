# Email Service

Nutrir sends transactional email (intake form links, appointment confirmations, etc.) via MailKit over Gmail SMTP. The implementation follows the standard Core/Infrastructure split: an interface in `Nutrir.Core` defines the contract, and a MailKit-backed class in `Nutrir.Infrastructure` provides the implementation.

## Architecture

```
Nutrir.Core
└── Interfaces/IEmailService.cs       ← contract consumed by application code

Nutrir.Infrastructure
├── Configuration/SmtpOptions.cs      ← typed options class
└── Services/EmailService.cs          ← MailKit implementation
```

Callers depend only on `IEmailService` and never reference MailKit or `SmtpOptions` directly.

### Key Files

| File | Project | Purpose |
|------|---------|---------|
| `src/Nutrir.Core/Interfaces/IEmailService.cs` | Core | Service interface |
| `src/Nutrir.Infrastructure/Configuration/SmtpOptions.cs` | Infrastructure | Typed options bound to the `Smtp` config section |
| `src/Nutrir.Infrastructure/Services/EmailService.cs` | Infrastructure | MailKit-based implementation |
| `src/Nutrir.Infrastructure/DependencyInjection.cs` | Infrastructure | DI registration |

## Interface

```csharp
public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody,
        CancellationToken ct = default);

    Task SendEmailAsync(string to, string? toName, string subject, string htmlBody,
        CancellationToken ct = default);
}
```

The two overloads differ only in whether a display name is included for the recipient. Both accept HTML content for `htmlBody`.

## Configuration

### SmtpOptions

`SmtpOptions` is a typed options class bound to the `Smtp` section of `appsettings.json`:

```csharp
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
```

### appsettings.json Defaults

Non-sensitive defaults are committed to `appsettings.json`:

```json
{
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "SenderName": "Nutrir"
  }
}
```

`SenderEmail`, `Username`, and `Password` are intentionally absent from `appsettings.json` and must be provided via environment variables.

### Docker Environment Variables

`docker-compose.yml` passes the three sensitive values into the `app` service:

```yaml
- Smtp__SenderEmail=${SMTP_SENDER_EMAIL:-}
- Smtp__Username=${SMTP_USERNAME:-}
- Smtp__Password=${SMTP_PASSWORD:-}
```

Set these in your `.env` file. See [Gmail SMTP Setup](gmail-smtp-setup.md) for instructions on obtaining these values and configuring DNS for deliverability.

### Full Configuration Reference

| Property | appsettings.json default | Environment variable | Description |
|----------|--------------------------|----------------------|-------------|
| `Host` | `smtp.gmail.com` | `Smtp__Host` | SMTP server hostname |
| `Port` | `587` | `Smtp__Port` | SMTP port (STARTTLS) |
| `SenderName` | `Nutrir` | `Smtp__SenderName` | Display name shown in "From" |
| `SenderEmail` | _(empty)_ | `Smtp__SenderEmail` | "From" email address |
| `Username` | _(empty)_ | `Smtp__Username` | SMTP authentication username |
| `Password` | _(empty)_ | `Smtp__Password` | SMTP authentication password / App Password |

## Implementation Details

`EmailService` creates a new `SmtpClient` (MailKit) per send operation. Each call to `SendInternalAsync`:

1. Builds a `MimeMessage` with the configured sender, the recipient address (and optional display name), subject, and an HTML body part.
2. Connects to the configured host and port using `SecureSocketOptions.StartTls` (STARTTLS on port 587).
3. Authenticates with the configured username and password.
4. Sends the message.
5. Disconnects in a `finally` block, regardless of send success or failure.

A structured log event at `Information` level is emitted on successful delivery, including the recipient address and subject.

Errors during connection, authentication, or sending propagate as exceptions to the caller. No retry logic is built in at the service layer.

## Dependency Injection

Registration is in `Nutrir.Infrastructure/DependencyInjection.cs`:

```csharp
services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
services.AddSingleton<IEmailService, EmailService>();
```

`EmailService` is registered as a **singleton**. The service is stateless — it creates and disposes a new `SmtpClient` per call rather than holding a long-lived connection, so there are no thread-safety or resource sharing concerns.

## Usage

Inject `IEmailService` into any service or Blazor component that needs to send email:

```csharp
public class IntakeFormService
{
    private readonly IEmailService _emailService;

    public IntakeFormService(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task SendIntakeLinkAsync(string clientEmail, string clientName, string link, CancellationToken ct)
    {
        var subject = "Please complete your intake form";
        var body = $"<p>Hi {clientName},</p><p>Please complete your intake form: <a href=\"{link}\">{link}</a></p>";

        await _emailService.SendEmailAsync(clientEmail, clientName, subject, body, ct);
    }
}
```

For callers that do not have a display name available, use the two-parameter overload:

```csharp
await _emailService.SendEmailAsync(recipientAddress, subject, htmlBody, ct);
```

## NuGet Packages

| Package | Purpose |
|---------|---------|
| `MailKit` | SMTP client, MIME construction |
| `MimeKit` | MIME message model (`MimeMessage`, `MailboxAddress`, `TextPart`) |

## Future Considerations

- **HTML email templates**: The service accepts raw HTML strings. A templating layer (e.g., Razor-rendered templates via `RazorLightEngine`, or a Fluid/Scriban template engine) could produce consistent branded messages without embedding HTML in service code.
- **Queue and retry**: For reliability, email sending should be moved off the request path. An outbox pattern (persist to a `PendingEmails` table, process via a background `IHostedService`) would survive transient SMTP failures and application restarts.
- **ASP.NET Core Identity integration**: Identity's `IEmailSender<TUser>` interface can be implemented on top of `IEmailService` to enable password reset and email confirmation flows without referencing MailKit from the Identity layer.
- **Alternative providers**: If volume outgrows Gmail's 2,000/day limit, `EmailService` can be replaced with a SendGrid or Postmark implementation — callers depend only on `IEmailService` and require no changes.
