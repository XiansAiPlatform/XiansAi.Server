# Secret Store Providers

Pluggable backend for the Secret Vault feature. The provider is selected once at application
startup via configuration and abstracts where (and how) secret **values** are stored. Metadata —
key, scope (tenant / agent / user / activation), `additionalData`, and audit fields — is always
persisted in the MongoDB `secret_vault` collection by `SecretVaultRepository`.

## Architecture

```
SecretVaultService ──► ISecretStoreProvider.SetAsync / GetAsync / DeleteAsync
                       │
                       ├── DatabaseSecretStoreProvider (default)
                       │     └─► MongoDB collection `secret_vault_values`
                       │           value = AES-256-GCM ciphertext (per-record key)
                       │
                       └── AzureKeyVaultSecretStoreProvider
                             └─► Azure Key Vault secret named "{prefix}{secretId}"
```

The `secretId` passed to the provider is the SecretVault Mongo `_id`. Tenant / agent / user
scoping is enforced **above** the provider in `SecretVaultScopeEnforcement`, so providers stay
simple and store-agnostic.

## Configuration

| Key | Required for | Default | Notes |
|-----|--------------|---------|-------|
| `SecretStore:Provider` | always | `database` | One of `database`, `azurekeyvault`. |
| `SecretStore:AzureKeyVault:VaultUri` | `azurekeyvault` | — | e.g. `https://my-vault.vault.azure.net/` |
| `SecretStore:AzureKeyVault:SecretNamePrefix` | optional | `xians-` | Prepended to each Key Vault secret name. |
| `EncryptionKeys:BaseSecret` | `database` | — | App-wide AES key material (already required for other features). |
| `EncryptionKeys:UniqueSecrets:SecretVaultKey` | legacy only | — | Used **only** to decrypt rows written before the provider abstraction; new rows derive a per-record key from `secretId`. |

Both colon (`SecretStore:Provider`) and double-underscore (`SecretStore__Provider`) formats are
honored, matching `CacheProviderFactory`.

## Database provider details

- Collection: `secret_vault_values` (separate from `secret_vault` to prevent accidental
  ciphertext leakage via list/get-by-id projections).
- Encryption: AES-256-GCM via `ISecureEncryptionService`.
- Per-record key derivation: the `secretId` itself is used as the unique-secret salt input to
  PBKDF2(BaseSecret, sha256(secretId), 200_000). Compromising one ciphertext does not weaken
  any other.
- Backwards compatibility: when no value document is found, the provider falls back to reading
  the legacy `encrypted_value` field on the metadata document and decrypting it with the legacy
  `SecretVaultKey`. Existing tenants therefore continue to work without a data migration; new
  writes go to `secret_vault_values`.

## Azure Key Vault provider details

- Auth: `DefaultAzureCredential` (managed identity in production, developer credentials locally).
- Required RBAC: `Key Vault Secrets Officer` (or any role granting `Get`, `Set`, `Delete` on
  secrets).
- Naming: `{prefix}{secretId}`. Prefix defaults to `xians-`. The full name must match
  `^[0-9a-zA-Z-]{1,127}$`.
- Soft-delete: `DeleteAsync` calls `StartDeleteSecretAsync`, which respects the vault's
  soft-delete and purge-protection policies. Re-creating a secret with the same name while a
  prior version is in the soft-deleted state will fail until the deleted secret is purged.

## Switching providers

Provider switches are **not** automatic data migrations. Existing values written by one provider
are not visible to the other. To migrate from `database` to `azurekeyvault`:

1. Read each existing value through the database provider (or directly).
2. Push it to Key Vault under the same `secretId`.
3. Switch `SecretStore:Provider` and redeploy.
4. Optionally clear the legacy `encrypted_value` field and the `secret_vault_values` collection.

A migration helper is intentionally out of scope for the initial release.
