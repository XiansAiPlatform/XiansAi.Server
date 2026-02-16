# Secret Vault Configuration Guide

This guide explains how to configure the Agent Secret Vault feature in your `appsettings.json` file.

## Overview

The Secret Vault supports multiple storage providers:
- **Database** (MongoDB) - Fully implemented with encryption at rest
- **Azure Key Vault** - Stub implementation (throws `NotImplementedException`)
- **AWS Secrets Manager** - Stub implementation (throws `NotImplementedException`)
- **HashiCorp Vault** - Stub implementation (throws `NotImplementedException`)

## Provider Selection

Configure the provider using the `SecretVault:Provider` setting:

```json
{
  "SecretVault": {
    "Provider": "database"
  }
}
```

### Supported Provider Values

- `"database"` - MongoDB with encryption at rest (fully implemented)
- `"azure"` - Azure Key Vault (not yet implemented)
- `"aws"` - AWS Secrets Manager (not yet implemented)
- `"hashicorp"` - HashiCorp Vault (not yet implemented)

## Encryption Keys Configuration

The Secret Vault uses the same encryption key pattern as `TenantOidcConfigService`. Configure encryption keys in your `appsettings.json`:

```json
{
  "EncryptionKeys": {
    "BaseSecret": "your-base-secret-key-here",
    "UniqueSecrets": {
      "TenantOidcSecretKey": "optional-tenant-oidc-specific-key",
      "SecretVaultKey": "optional-secret-vault-specific-key"
    }
  }
}
```

### Encryption Key Behavior

1. **Preferred**: If `EncryptionKeys:UniqueSecrets:SecretVaultKey` is configured, it will be used for secret vault encryption.
2. **Fallback**: If `SecretVaultKey` is not configured, the system falls back to `EncryptionKeys:BaseSecret`.
3. **Required**: At minimum, `EncryptionKeys:BaseSecret` must be configured.

### Security Recommendations

- **Never commit encryption keys to source control**
- Store encryption keys in:
  - Environment variables
  - Azure Key Vault
  - AWS Secrets Manager
  - HashiCorp Vault
  - Secure configuration management systems

### Example: Using Environment Variables

Set environment variables:

```bash
export EncryptionKeys__BaseSecret="your-base-secret"
export EncryptionKeys__UniqueSecrets__SecretVaultKey="your-vault-specific-secret"
```

Or in `appsettings.json` (for local development only):

```json
{
  "EncryptionKeys": {
    "BaseSecret": "${ENCRYPTION_BASE_SECRET}",
    "UniqueSecrets": {
      "SecretVaultKey": "${ENCRYPTION_SECRET_VAULT_KEY}"
    }
  }
}
```

## Database Provider Configuration

When using the `database` provider, no additional configuration is required beyond:
1. MongoDB connection string (configured separately)
2. Encryption keys (as described above)

### Example Configuration

```json
{
  "SecretVault": {
    "Provider": "database"
  },
  "EncryptionKeys": {
    "BaseSecret": "your-base-secret-key",
    "UniqueSecrets": {
      "SecretVaultKey": "your-secret-vault-key"
    }
  }
}
```

## Azure Key Vault Provider Configuration (Future)

When Azure Key Vault support is implemented, configure it as follows:

```json
{
  "SecretVault": {
    "Provider": "azure",
    "Azure": {
      "VaultUrl": "https://your-vault-name.vault.azure.net/",
      "TenantId": "optional-azure-ad-tenant-id",
      "ClientId": "optional-azure-ad-client-id",
      "ClientSecret": "optional-azure-ad-client-secret"
    }
  }
}
```

### Azure Key Vault Authentication

Azure Key Vault supports multiple authentication methods:
- **Managed Identity** (recommended for Azure-hosted applications)
- **Service Principal** (using ClientId/ClientSecret)
- **Certificate-based authentication**

If `TenantId`, `ClientId`, and `ClientSecret` are not provided, the Azure SDK will attempt to use Managed Identity.

## AWS Secrets Manager Provider Configuration (Future)

When AWS Secrets Manager support is implemented, configure it as follows:

```json
{
  "SecretVault": {
    "Provider": "aws",
    "Aws": {
      "Region": "us-east-1",
      "AccessKeyId": "optional-access-key-id",
      "SecretAccessKey": "optional-secret-access-key",
      "ProfileName": "optional-aws-profile-name"
    }
  }
}
```

### AWS Authentication

AWS Secrets Manager supports multiple authentication methods:
- **IAM Role** (recommended for AWS-hosted applications)
- **Access Keys** (using AccessKeyId/SecretAccessKey)
- **AWS Profile** (using ProfileName)

If credentials are not provided, the AWS SDK will attempt to use:
1. Environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`)
2. AWS credentials file (`~/.aws/credentials`)
3. IAM role (when running on EC2/ECS/Lambda)

## HashiCorp Vault Provider Configuration (Future)

When HashiCorp Vault support is implemented, configure it as follows:

```json
{
  "SecretVault": {
    "Provider": "hashicorp",
    "HashiCorp": {
      "VaultUrl": "https://vault.example.com:8200",
      "Token": "optional-vault-token",
      "AuthMethod": "token",
      "MountPath": "secret",
      "RoleId": "optional-approle-role-id",
      "SecretId": "optional-approle-secret-id"
    }
  }
}
```

### HashiCorp Vault Authentication

HashiCorp Vault supports multiple authentication methods:
- **Token** (using Token)
- **AppRole** (using RoleId/SecretId)
- **AWS** (using AWS IAM)
- **Azure** (using Azure Managed Identity)
- **Kubernetes** (using Kubernetes service account)

## Complete Configuration Example

Here's a complete example configuration for the database provider:

```json
{
  "SecretVault": {
    "Provider": "database"
  },
  "EncryptionKeys": {
    "BaseSecret": "your-base-secret-key-minimum-32-characters-long",
    "UniqueSecrets": {
      "TenantOidcSecretKey": "optional-tenant-oidc-key",
      "SecretVaultKey": "optional-secret-vault-key"
    }
  },
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017/xiansai"
  }
}
```

## Provider Switching

### Important Notes

- **Provider is exclusive**: Only one provider can be active at runtime
- **No data migration**: Switching providers does not automatically migrate secrets
- **Database provider**: Stores encrypted secrets in MongoDB
- **Key Vault providers**: Store entire secret objects as JSON in the vault (no MongoDB records)

### Switching from Database to Key Vault

1. Export all secrets from the database provider
2. Switch provider configuration
3. Import secrets into the new provider
4. Verify secrets are accessible
5. Optionally delete secrets from the old provider

### Switching from Key Vault to Database

1. Export all secrets from the key vault provider
2. Switch provider configuration
3. Import secrets into the database provider
4. Verify secrets are accessible
5. Optionally delete secrets from the old provider

## Validation

The system validates configuration on startup:

- ✅ Provider must be one of: `database`, `azure`, `aws`, `hashicorp`
- ✅ `EncryptionKeys:BaseSecret` must be configured (for database provider)
- ✅ Azure Key Vault URL must be valid (when using Azure provider)
- ✅ AWS Region must be valid (when using AWS provider)
- ✅ HashiCorp Vault URL must be valid (when using HashiCorp provider)

## Troubleshooting

### Error: "EncryptionKeys:BaseSecret is not configured"

**Solution**: Add `EncryptionKeys:BaseSecret` to your configuration.

### Error: "Unknown secret vault provider: {provider}"

**Solution**: Ensure `SecretVault:Provider` is set to one of: `database`, `azure`, `aws`, `hashicorp`.

### Error: "NotImplementedException: Azure Key Vault provider is not yet implemented"

**Solution**: The Azure Key Vault provider is not yet implemented. Use `database` provider instead.

### Error: "Failed to decrypt secret"

**Solution**: 
- Verify encryption keys match the keys used to encrypt the secrets
- Check that `EncryptionKeys:UniqueSecrets:SecretVaultKey` or `EncryptionKeys:BaseSecret` is correct
- Ensure the secret was encrypted with the same key configuration

## Security Best Practices

1. **Use unique encryption keys**: Configure `SecretVaultKey` for additional security isolation
2. **Rotate keys regularly**: Plan for key rotation (requires re-encryption of all secrets)
3. **Store keys securely**: Never commit keys to source control
4. **Use environment variables**: Prefer environment variables over configuration files
5. **Limit access**: Restrict access to encryption keys to only necessary personnel/systems
6. **Audit access**: Monitor and log access to secrets
7. **Use Key Vault providers**: For production, prefer Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault over database storage

## Related Documentation

- [Agent Secret Vault README](./AGENT_SECRET_VAULT_README.md) - Complete feature documentation
- [Tenant OIDC Configuration](../Shared/Services/TenantOidcConfigService.cs) - Similar encryption pattern reference

