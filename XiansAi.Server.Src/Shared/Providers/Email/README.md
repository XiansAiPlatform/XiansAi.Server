# Email

The server sends email (for example, user invitations) through `IEmailService` over SMTP. The server env sets a default SMTP server, and each tenant can override it with its own SMTP server from Agent Studio (**Tenant Settings → Email**).

## Which SMTP server is used

| Server default (`Email:Smtp:Host`) | Tenant SMTP config | Result |
|---|---|---|
| Not set | None | Email is **off**. Sends are skipped and logged, not failed. |
| Set | None | The **server default** is used. |
| Any | Saved | The **tenant's SMTP** server is used. |

Removing the tenant config (`DELETE .../smtp-config`) reverts the tenant to the server default.

## Server default

```bash
Email__Smtp__Host=smtp.example.com
Email__Smtp__Port=587
Email__Smtp__Security=StartTls
Email__Smtp__Username=user@example.com
Email__Smtp__Password=...
Email__Smtp__SenderEmail=noreply@example.com
Email__Smtp__SenderName=Xians
```

- `Host` and `SenderEmail` are required. `Username` and `Password` are only needed if the server requires authentication.
- `Security` is one of `None`, `StartTls` (usually port 587), `SslOnConnect` (usually port 465) or `Auto`.
- If `Host` is set but the other settings are invalid, the server fails at startup with a clear error.

For local testing, run [Mailpit](https://mailpit.axllent.org/) (`docker run -p 1025:1025 -p 8025:8025 axllent/mailpit`). Set `Host=localhost`, `Port=1025` and `Security=None`, then read the emails at http://localhost:8025.

## Tenant SMTP override

Tenant admins manage it in Agent Studio, or through the Admin API:

| Method | Path | Does |
|---|---|---|
| `GET` | `/api/v1/admin/tenants/{tenantId}/smtp-config` | Returns `source` (`Tenant`, `Server`, or `None` when email is off) and the tenant's settings. The password is never returned; `hasPassword` says whether one is saved. |
| `PUT` | `/api/v1/admin/tenants/{tenantId}/smtp-config` | Saves the tenant's settings. A blank `password` keeps the saved one. |
| `DELETE` | `/api/v1/admin/tenants/{tenantId}/smtp-config` | Removes the tenant's settings, so the server default applies again. |
| `POST` | `/api/v1/admin/tenants/{tenantId}/smtp-config/test` | Sends a test email to `to` with the given settings, without saving them. |

Example `PUT` body:

```json
{
  "host": "smtp.example.com",
  "port": 587,
  "security": "StartTls",
  "username": "user@example.com",
  "password": "...",
  "senderEmail": "noreply@example.com",
  "senderName": "Acme"
}
```

The password is encrypted at rest with `EncryptionKeys:UniqueSecrets:TenantSmtpSecretKey`, or with `EncryptionKeys:BaseSecret` when that key is not set. These actions need the `tenant.smtpConfig.*` capabilities, which TenantAdmin has by default. Changes are recorded as `tenant.smtp.updated` and `tenant.smtp.deleted` events.

## Sending email from your code

Inject `IEmailService` and pass the tenant. The service picks the right SMTP server.

```csharp
public class MyService(IEmailService emailService)
{
    public async Task NotifyAsync(string tenantId, string userEmail)
    {
        await emailService.SendEmailAsync(tenantId, userEmail, "Your report is ready", "<p>Open Xians to view it.</p>", isHtml: true);
    }

    public async Task NotifyAdminsAsync(string tenantId, IEnumerable<string> admins)
    {
        // Skip work you only need for email when email is off.
        if (!await emailService.IsEnabledAsync(tenantId)) return;

        await emailService.SendEmailAsync(tenantId, admins, "Agent stopped", "The agent stopped unexpectedly.");
    }
}
```

- When email is off, `SendEmailAsync` does nothing and logs it.
- When sending fails, it throws. Catch the exception if email is optional for your feature.
- Pass `tenantId: null` for platform-level email; it always uses the server default.

## Files

```text
Shared/Providers/Email/
├── SmtpSettings.cs       # SMTP connection + sender settings, validation
└── SmtpEmailSender.cs    # Sends via MailKit (server default and tenant overrides)

Shared/Configuration/SharedServices.cs             # Reads Email:Smtp, registers IEmailService
Shared/Services/EmailService.cs                    # IEmailService: picks tenant SMTP or server default
Shared/Services/TenantSmtpConfigService.cs         # Tenant SMTP get/save/delete/test
Shared/Repositories/TenantSmtpConfigRepository.cs  # tenant_smtp_config collection (encrypted password)
```
