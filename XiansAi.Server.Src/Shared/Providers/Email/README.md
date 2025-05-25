# Email Provider Pattern

## Overview

The Email Provider Pattern in XiansAi.Server provides a flexible, priority-based email system that automatically selects the best available email provider. The system gracefully falls back from production email services (Azure Communication Services) to development/testing providers (Console) when the production service is unavailable.

## Architecture

### Core Components

```text
Shared/Providers/Email/
├── IEmailProvider.cs                    # Main email abstraction
├── IEmailProviderRegistration.cs        # Self-registration interface
├── AzureCommunicationEmailProvider.cs   # Azure Communication Services implementation
├── ConsoleEmailProvider.cs              # Console/dummy implementation
└── EmailProviderFactory.cs              # Factory with priority-based selection
```

### 1. Email Provider Interface

```csharp
public interface IEmailProvider
{
    Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false);
    Task<bool> SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false);
}
```

### 2. Provider Registration Interface

```csharp
public interface IEmailProviderRegistration
{
    static abstract string ProviderName { get; }
    static abstract int Priority { get; }
    static abstract bool CanRegister(IConfiguration configuration);
    static abstract void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
```

### 3. Email Service Interface

```csharp
public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = false);
    Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false);
}
```

### 4. Provider Implementations

#### Azure Communication Services Provider (Priority: 1)

- **High Priority**: Preferred when available
- **Production Ready**: Suitable for production environments
- **External Service**: Requires Azure Communication Services configuration
- **Configuration Required**: Connection string and sender email

#### Console Provider (Priority: 100)

- **Fallback**: Used when Azure Communication Services is unavailable
- **Development/Testing**: Prints emails to console instead of sending
- **No Dependencies**: Always available
- **Debugging**: Useful for development and testing scenarios

## Priority System

### How Priorities Work

- **Lower numbers = Higher priority**
- **Registration**: Providers register in priority order
- **Creation**: Factory tries providers in priority order until one succeeds

### Current Priorities

| Provider | Priority | Use Case |
|----------|----------|----------|
| Azure Communication Services | 1 | Production, real email sending |
| Console | 100 | Development, testing, fallback |

## Configuration

### Azure Communication Services Configuration

```json
{
  "CommunicationServices": {
    "ConnectionString": "endpoint=https://your-resource.communication.azure.com/;accesskey=your-access-key",
    "SenderEmail": "noreply@yourdomain.com"
  }
}
```

### No Configuration Required for Console

The console provider is always available as a fallback and requires no configuration.

## Usage

### Application Startup

```csharp
// In Program.cs or Startup.cs
// Email providers are automatically registered when you call:
services.AddInfrastructureServices(configuration);

// This automatically handles:
// - EmailProviderFactory.RegisterProviders(services, configuration);
// - services.AddSingleton<IEmailProviderFactory, EmailProviderFactory>();
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

### Direct Provider Usage (Advanced)

```csharp
public class AdvancedEmailService
{
    private readonly IEmailProvider _emailProvider;

    public AdvancedEmailService(IEmailProviderFactory factory)
    {
        _emailProvider = factory.CreateEmailProvider();
    }

    public async Task<bool> TrySendEmailAsync(string to, string subject, string body)
    {
        // Direct provider usage with success/failure handling
        return await _emailProvider.SendEmailAsync(to, subject, body);
    }
}
```

## Adding New Providers

### Step 1: Implement the Provider

```csharp
public class SendGridEmailProvider : IEmailProvider, IEmailProviderRegistration
{
    public static string ProviderName => "SendGrid";
    public static int Priority => 2; // Between Azure and Console

    public static bool CanRegister(IConfiguration configuration)
    {
        return !string.IsNullOrEmpty(configuration["SendGrid:ApiKey"]);
    }

    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var apiKey = configuration["SendGrid:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            services.AddSingleton<ISendGridClient>(sp => new SendGridClient(apiKey));
        }
    }

    // Implement IEmailProvider methods...
}
```

### Step 2: Add to Factory

```csharp
private static readonly EmailProviderDefinition[] Providers = new[]
{
    // Existing providers...
    new EmailProviderDefinition
    {
        Name = SendGridEmailProvider.ProviderName,
        Priority = SendGridEmailProvider.Priority,
        CanRegister = SendGridEmailProvider.CanRegister,
        RegisterServices = SendGridEmailProvider.RegisterServices,
        CreateProvider = serviceProvider => {
            var sendGridClient = serviceProvider.GetService<ISendGridClient>();
            if (sendGridClient != null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<SendGridEmailProvider>>();
                return new SendGridEmailProvider(sendGridClient, logger);
            }
            return null;
        }
    }
};
```

### Step 3: Add Configuration

```json
{
  "SendGrid": {
    "ApiKey": "your-sendgrid-api-key",
    "SenderEmail": "noreply@yourdomain.com"
  }
}
```

**Result**: SendGrid will automatically be tried after Azure Communication Services but before Console.
