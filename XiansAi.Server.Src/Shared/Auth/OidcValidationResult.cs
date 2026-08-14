namespace Shared.Auth;

/// <summary>
/// Outcome of validating a JWT against a tenant's OIDC rules.
/// </summary>
public class OidcValidationResult
{
    public bool Success { get; init; }

    /// <summary>Provider-prefixed id (`provider|subject`), used for claims and display.</summary>
    public string? CanonicalUserId { get; init; }

    /// <summary>
    /// The raw provider subject. This is the form the users collection is keyed on, so it is the
    /// only value usable for looking up or provisioning the user record.
    /// </summary>
    public string? ProviderUserId { get; init; }

    /// <summary>
    /// Normalized OIDC authority the signing keys were fetched from, which is what
    /// <see cref="ProviderUserId"/> is only unique within. Used to pin a user record to one
    /// provider so a second provider cannot claim the same subject.
    ///
    /// Not the `iss` claim: the expected issuer is tenant-configurable and can name any string,
    /// while the authority has to actually serve the discovery document that yielded the keys.
    /// </summary>
    public string? ProviderAuthority { get; init; }

    public string? Email { get; init; }

    /// <summary>
    /// Whether the token was checked against the audiences the provider declared, rather than being
    /// accepted on its issuer's signature alone.
    ///
    /// False means only that the issuer signed it — it may have been minted for an entirely
    /// different application at that same identity provider. Anything that treats holding a token
    /// as the tenant's own statement about the caller needs this to be true, because that is what
    /// the audience says and the signature does not.
    /// </summary>
    public bool AudienceValidated { get; init; }

    public string? Name { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// When the token itself expires. Bounds how long a successful result may be reused, so that
    /// caching can never extend a token's lifetime.
    /// </summary>
    public DateTimeOffset? TokenExpiresAt { get; init; }

    public static OidcValidationResult Fail(string error) =>
        new() { Success = false, Error = error };

    public static OidcValidationResult Ok(
        string canonicalUserId,
        string providerUserId,
        string? providerAuthority,
        string? email,
        string? name,
        DateTimeOffset? tokenExpiresAt = null,
        bool audienceValidated = false) =>
        new()
        {
            Success = true,
            CanonicalUserId = canonicalUserId,
            ProviderUserId = providerUserId,
            ProviderAuthority = providerAuthority,
            Email = email,
            AudienceValidated = audienceValidated,
            Name = name,
            TokenExpiresAt = tokenExpiresAt
        };
}

public interface IDynamicOidcValidator
{
    /// <summary>
    /// Validates a JWT against the OIDC rules configured for <paramref name="tenantId"/>.
    /// </summary>
    Task<OidcValidationResult> ValidateAsync(string tenantId, string token);
}
