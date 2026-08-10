# ADR-0007: Secrets Encryption at Rest

**Status:** Proposed

**Date:** 2026-07-16

## Context

The XiansAi Server stores sensitive credentials in MongoDB:
- Admin and agent API keys
- OAuth tokens for external platform integrations (Slack, Teams)
- Signing secrets for webhook verification
- Customer-provided secrets in the Secret Vault

Storing these secrets in plaintext violates security best practices and compliance requirements (SOC 2, GDPR, etc.). A database breach would expose all tenant secrets.

We needed a solution that:
- Encrypts secrets before persistence
- Decrypts secrets transparently on retrieval
- Supports key rotation
- Uses industry-standard algorithms (AES-256-GCM)

## Decision

We implement **secrets encryption at rest** using an encryption service:

1. All sensitive fields are encrypted using AES-256-GCM before being saved to MongoDB
2. The encryption service uses a base secret (from environment or key vault) and optional per-tenant unique secrets
3. Repositories and services call `encryptionService.EncryptAsync()` before persistence and `encryptionService.DecryptAsync()` after retrieval
4. Configuration values stored in `appsettings.json` or environment variables are not encrypted (they are not persisted to the database)

Example:
```csharp
// Before saving
var encryptedToken = await _encryptionService.EncryptAsync(plainToken, tenantId);
appInstance.Configuration["botToken"] = encryptedToken;
await _repository.SaveAsync(appInstance);

// After retrieval
var encryptedToken = appInstance.Configuration["botToken"];
var plainToken = await _encryptionService.DecryptAsync(encryptedToken, tenantId);
```

See `docs/SECRETS_ENCRYPTION.md` for implementation details.

## Consequences

**Positive:**
- Defense in depth: even if MongoDB is compromised, secrets are encrypted
- Compliance: meets requirements for secrets protection
- Key rotation support: can re-encrypt with new keys
- Transparent to application logic: encryption is encapsulated in the service

**Negative:**
- Performance overhead: encryption/decryption on every secret access
- Complexity: key management and rotation must be handled carefully
- Debugging difficulty: cannot inspect secrets directly in the database

**Mitigations:**
- Cache decrypted secrets in memory for short periods (with caution)
- Use Azure Key Vault or AWS Secrets Manager for base secret storage
- Provide tooling for key rotation and re-encryption
- Log encryption failures clearly for troubleshooting
