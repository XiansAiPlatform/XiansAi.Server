using Shared.Data.Models;

namespace Shared.Services;

/// <summary>
/// Encrypts and decrypts tenant metadata values of type <see cref="MetadataType.Secret"/>
/// using the existing <see cref="ISecureEncryptionService"/> (AES-256-GCM keyed from
/// EncryptionKeys:BaseSecret). The tenant's immutable TenantId is used as the unique
/// secret so each tenant gets an isolated derived key, mirroring the per-record pattern
/// used by the secret vault.
/// </summary>
public interface ITenantMetadataProtector
{
    /// <summary>Returns a new list with Secret values encrypted. Never mutates the input.</summary>
    List<TenantMetadata>? Protect(List<TenantMetadata>? metadata, string tenantId);

    /// <summary>Returns a new list with Secret values decrypted. Never mutates the input.</summary>
    List<TenantMetadata>? Unprotect(List<TenantMetadata>? metadata, string tenantId);
}

public class TenantMetadataProtector : ITenantMetadataProtector
{
    private readonly ISecureEncryptionService _encryption;

    public TenantMetadataProtector(ISecureEncryptionService encryption)
    {
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
    }

    public List<TenantMetadata>? Protect(List<TenantMetadata>? metadata, string tenantId)
    {
        return Transform(metadata, tenantId, encrypt: true);
    }

    public List<TenantMetadata>? Unprotect(List<TenantMetadata>? metadata, string tenantId)
    {
        return Transform(metadata, tenantId, encrypt: false);
    }

    private List<TenantMetadata>? Transform(List<TenantMetadata>? metadata, string tenantId, bool encrypt)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return metadata;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID must be provided to protect metadata", nameof(tenantId));
        }

        var result = new List<TenantMetadata>(metadata.Count);
        foreach (var item in metadata)
        {
            var copy = item.Copy();
            if (copy.Type == MetadataType.Secret)
            {
                try
                {
                    copy.Value = encrypt
                        ? _encryption.Encrypt(copy.Value, tenantId)
                        : _encryption.Decrypt(copy.Value, tenantId);
                }
                catch (Exception ex)
                {
                    // Never include the value or any key material in the error.
                    var operation = encrypt ? "encrypt" : "decrypt";
                    throw new InvalidOperationException(
                        $"Failed to {operation} metadata value for key '{copy.Key}' of tenant '{tenantId}'", ex);
                }
            }
            result.Add(copy);
        }
        return result;
    }
}
