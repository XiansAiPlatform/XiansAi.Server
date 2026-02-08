# Secrets Encryption Guide

## Overview

This application uses **MongoDB Field-Level Encryption** to protect sensitive data like passwords, tokens, and API keys. All secrets are encrypted at rest using AES-256 encryption before being stored in MongoDB.

## Architecture

### Components

1. **AppIntegrationSecrets Model** (`Features/AppsApi/Models/AppIntegrationSecrets.cs`)
   - Contains all sensitive fields (passwords, tokens, webhook secrets)
   - Provides masking functionality for API responses
   - Extensible via `CustomSecrets` dictionary

2. **ISecretsEncryptionService** (`Shared/Services/ISecretsEncryptionService.cs`)
   - Interface for encryption/decryption operations
   - Allows swapping implementations (e.g., Azure Key Vault in future)

3. **SecretsEncryptionService** (`Shared/Services/SecretsEncryptionService.cs`)
   - AES-256 encryption implementation
   - Supports key rotation via Key ID tracking
   - Encrypts/decrypts entire `AppIntegrationSecrets` objects

4. **Repository Layer** (`Features/AppsApi/Repositories/AppIntegrationRepository.cs`)
   - Automatically encrypts secrets before saving to database
   - Automatically decrypts secrets after loading from database
   - Transparent to application code

### Data Flow

```
Create/Update:
  AppIntegration.Secrets (plain) 
  → Repository.EncryptSecrets() 
  → AppIntegration.SecretsEncrypted (encrypted base64) 
  → MongoDB

Retrieve:
  MongoDB 
  → AppIntegration.SecretsEncrypted (encrypted base64)
  → Repository.DecryptSecrets()
  → AppIntegration.Secrets (plain)
  → API Response (masked)
```

## Configuration Setup

### 1. Generate Encryption Key

Use OpenSSL to generate a secure 256-bit (32-byte) encryption key:

```bash
openssl rand -base64 32
```

Example output:
```
vK8E2xN5pQ7wR9mT3nY6uH8jL0cF4aS1dG2hJ5kM9oP=
```

### 2. Add to Configuration

Add the encryption key to your `appsettings.json` or environment-specific configuration:

#### appsettings.json
```json
{
  "Encryption": {
    "SecretsKey": "vK8E2xN5pQ7wR9mT3nY6uH8jL0cF4aS1dG2hJ5kM9oP=",
    "KeyId": "v1"
  }
}
```

#### appsettings.Production.json (Use different key in production!)
```json
{
  "Encryption": {
    "SecretsKey": "YOUR_PRODUCTION_KEY_HERE",
    "KeyId": "prod-v1"
  }
}
```

#### Environment Variables (Recommended for Production)
```bash
export Encryption__SecretsKey="vK8E2xN5pQ7wR9mT3nY6uH8jL0cF4aS1dG2hJ5kM9oP="
export Encryption__KeyId="prod-v1"
```

### 3. Register Service

The service is automatically registered in your DI container. Ensure you have this in your startup/registration code:

```csharp
services.AddSingleton<ISecretsEncryptionService, SecretsEncryptionService>();
```

## Security Best Practices

### Key Management

1. **Never commit keys to source control**
   - Add `appsettings.*.json` files with keys to `.gitignore`
   - Use environment variables or secret managers in production

2. **Use different keys per environment**
   - Development: Local configuration file
   - Staging: Environment variables or Azure Key Vault
   - Production: Azure Key Vault or AWS Secrets Manager

3. **Rotate keys periodically**
   - The `KeyId` field enables key rotation tracking
   - Old data will log warnings but still decrypt with current key
   - Plan migration strategy for key rotation

### Key Storage Options

#### Option 1: Environment Variables (Current)
```bash
# Good for containers and cloud deployments
export Encryption__SecretsKey="..."
export Encryption__KeyId="v1"
```

#### Option 2: Azure Key Vault (Future Enhancement)
```csharp
// Future implementation
services.AddSingleton<ISecretsEncryptionService, AzureKeyVaultSecretsService>();
```

#### Option 3: AWS Secrets Manager (Future Enhancement)
```csharp
// Future implementation
services.AddSingleton<ISecretsEncryptionService, AwsSecretsManagerService>();
```

## Usage in Code

### Storing Secrets

```csharp
var integration = new AppIntegration
{
    // ... other fields ...
    Secrets = new AppIntegrationSecrets
    {
        WebhookSecret = GenerateRandomSecret(),
        SlackSigningSecret = request.SigningSecret,
        SlackBotToken = request.BotToken,
        TeamsAppPassword = request.AppPassword
    }
};

// Repository automatically encrypts before saving
await repository.CreateAsync(integration);
```

### Retrieving Secrets

```csharp
// Repository automatically decrypts after loading
var integration = await repository.GetByIdAsync(id);

// Access decrypted secrets
var botToken = integration.Secrets.SlackBotToken;

// API responses show masked values
var response = AppIntegrationResponse.FromEntity(integration, baseUrl);
// response.Secrets.SlackBotToken = "xoxb****1234"
```

### Adding New Secret Fields

1. Add property to `AppIntegrationSecrets`:
```csharp
[JsonPropertyName("myNewSecret")]
public string? MyNewSecret { get; set; }
```

2. Update the `Mask()` method:
```csharp
public AppIntegrationSecrets Mask()
{
    return new AppIntegrationSecrets
    {
        // ... existing fields ...
        MyNewSecret = MaskSecret(MyNewSecret)
    };
}
```

3. Use it in your code:
```csharp
integration.Secrets.MyNewSecret = "sensitive-value";
```

## Migration from Configuration Dictionary

If you have existing integrations with secrets in the `Configuration` dictionary, migrate them:

```csharp
// Old way (DEPRECATED)
integration.Configuration["botToken"] = "xoxb-123...";

// New way (ENCRYPTED)
integration.Secrets.SlackBotToken = "xoxb-123...";
```

## Monitoring and Troubleshooting

### Check if Encryption is Working

```csharp
// After creating integration, check MongoDB directly
// The secrets_encrypted field should contain base64 encrypted data
// Example: "djF2AAAAABQAAAACPfK3m..."
```

### Common Issues

1. **"Encryption:SecretsKey not found in configuration"**
   - Solution: Add the encryption key to appsettings.json or environment variables

2. **"Encryption key must be 32 bytes (256 bits)"**
   - Solution: Generate a new key using `openssl rand -base64 32`

3. **"Failed to decrypt data"**
   - Possible causes:
     - Wrong encryption key
     - Data corrupted
     - Key was changed after data was encrypted
   - Solution: Verify the key matches what was used to encrypt

### Logging

The service logs encryption/decryption operations:
- `Information`: Service initialization with Key ID
- `Warning`: Key ID mismatch (may indicate key rotation needed)
- `Error`: Encryption/decryption failures

## Performance Considerations

- **Encryption overhead**: ~1-2ms per operation (negligible for API calls)
- **Memory**: Encrypted data is ~33% larger than plaintext (base64 encoding)
- **Database**: Encrypted field is indexed for fast lookups

## Future Enhancements

### Key Rotation Support

```csharp
// Future: Support multiple keys for rotation
public interface ISecretsEncryptionService
{
    string Encrypt(string plainText, string? keyId = null);
    string Decrypt(string encryptedText); // Auto-detects key from data
}
```

### External Key Management

Replace `SecretsEncryptionService` with cloud provider implementations:

```csharp
// Azure Key Vault
services.AddSingleton<ISecretsEncryptionService, AzureKeyVaultSecretsService>();

// AWS Secrets Manager
services.AddSingleton<ISecretsEncryptionService, AwsSecretsManagerService>();
```

## Testing

Generate a test encryption key for development:

```bash
# Development only - DO NOT use in production
openssl rand -base64 32 > encryption-key-dev.txt
```

Add to development configuration:
```json
{
  "Encryption": {
    "SecretsKey": "YOUR_DEV_KEY_FROM_FILE",
    "KeyId": "dev-v1"
  }
}
```

## Compliance

This encryption implementation helps meet:
- **PCI DSS**: Protects cardholder data at rest
- **GDPR**: Encrypts personal data
- **SOC 2**: Data protection controls
- **HIPAA**: PHI encryption requirements (if applicable)

## Questions?

For questions or issues, refer to:
- [MongoDB Encryption Documentation](https://www.mongodb.com/docs/manual/core/security-client-side-encryption/)
- [.NET Cryptography Best Practices](https://docs.microsoft.com/en-us/dotnet/standard/security/cryptography-model)
