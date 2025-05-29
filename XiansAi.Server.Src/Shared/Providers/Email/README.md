# Email Provider Pattern

## Overview

The Email Provider Pattern in XiansAi.Server provides a simple, configuration-based email system that selects the appropriate email provider based on application settings. The system supports multiple email providers including Azure Communication Services for production and Console provider for development/testing.

## Architecture

### Core Components

```text
Shared/Providers/Email/
├── IEmailProvider.cs                    # Main email abstraction
├── AzureCommunicationEmailProvider.cs   # Azure Communication Services implementation
├── ConsoleEmailProvider.cs              # Console/dummy implementation
└── EmailProviderFactory.cs              # Simple factory with configuration-based selection
```

### 1. Email Provider Interface

```csharp
public interface IEmailProvider
{
    Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false);
    Task<bool> SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false);
}
```

### 2. Email Service Interface

```csharp
public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = false);
    Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false);
}
```

### 3. Provider Implementations

#### Azure Communication Services Provider

- **Production Ready**: Suitable for production environments
- **External Service**: Requires Azure Communication Services configuration
- **Configuration Required**: Connection string and sender email

#### Console Provider

- **Development/Testing**: Prints emails to console instead of sending
- **No Dependencies**: Always available
- **Debugging**: Useful for development and testing scenarios

## Configuration

### Email Provider Configuration

```json
{
  "Email": {
    "Provider": "azure", // or "console"
    "Azure": {
      "ConnectionString": "endpoint=https://your-resource.communication.azure.com/;accesskey=your-access-key",
      "SenderEmail": "noreply@yourdomain.com"
    }
  }
}
```

### Configuration Options

| Provider | Configuration Key | Required Settings |
|----------|------------------|-------------------|
| Azure Communication Services | `"azure"` | `Email:Azure:ConnectionString`, `Email:Azure:SenderEmail` |
| Console | `"console"` | None |

## Usage

### Application Startup

```csharp
// In Program.cs or Startup.cs
// Email providers are automatically registered when you call:
services.AddInfrastructureServices(configuration);

// This automatically handles:
// - EmailProviderFactory.RegisterProvider(services, configuration);
// - services.AddScoped<IEmailService, EmailService>();
```

### Service Usage

```csharp
public class MyService
{
    private readonly IEmailService _emailService;

    public MyService(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task SendWelcomeEmailAsync(string userEmail, string userName)
    {
        var subject = "Welcome to XiansAi!";
        var body = $"<h1>Welcome {userName}!</h1><p>Thank you for joining XiansAi.</p>";
        
        // Same API regardless of underlying provider
        await _emailService.SendEmailAsync(userEmail, subject, body, isHtml: true);
    }

    public async Task SendBulkNotificationAsync(IEnumerable<string> recipients, string message)
    {
        var subject = "Important Notification";
        
        // Send to multiple recipients
        await _emailService.SendEmailAsync(recipients, subject, message);
    }
}
```

## Adding New Providers

### Step 1: Implement the Provider

```csharp
public class SendGridEmailProvider : IEmailProvider
{
    private readonly ISendGridClient _sendGridClient;
    private readonly string _senderEmail;
    private readonly ILogger<SendGridEmailProvider> _logger;

    public SendGridEmailProvider(
        ISendGridClient sendGridClient, 
        string senderEmail,
        ILogger<SendGridEmailProvider> logger)
    {
        _sendGridClient = sendGridClient;
        _senderEmail = senderEmail;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false)
    {
        // Implement SendGrid email sending logic
        // ...
    }

    public async Task<bool> SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        // Implement SendGrid bulk email sending logic
        // ...
    }
}
```

### Step 2: Add to Factory

```csharp
// In EmailProviderFactory.RegisterProvider method, add a new case:
case "sendgrid":
    var apiKey = configuration["Email:SendGrid:ApiKey"];
    var senderEmail = configuration["Email:SendGrid:SenderEmail"];
    if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(senderEmail))
    {
        throw new InvalidOperationException("SendGrid email provider requires Email:SendGrid:ApiKey and Email:SendGrid:SenderEmail");
    }
    services.AddSingleton<ISendGridClient>(sp => new SendGridClient(apiKey));
    services.AddScoped<IEmailProvider, SendGridEmailProvider>(sp =>
    {
        var sendGridClient = sp.GetRequiredService<ISendGridClient>();
        var logger = sp.GetRequiredService<ILogger<SendGridEmailProvider>>();
        return new SendGridEmailProvider(sendGridClient, senderEmail, logger);
    });
    break;
```

### Step 3: Add Configuration

```json
{
  "Email": {
    "Provider": "sendgrid",
    "SendGrid": {
      "ApiKey": "your-sendgrid-api-key",
      "SenderEmail": "noreply@yourdomain.com"
    }
  }
}
```
