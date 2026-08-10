using System.Security.Claims;
using CredentialType = Shared.Auth.UserType;

namespace Features.UserApi.Auth;

/// <summary>
/// Claim types the UserApi authentication handlers emit so that the matching authorization
/// handlers can restore the tenant context from the authenticated principal.
/// </summary>
public static class UserApiClaimTypes
{
    /// <summary>The tenant the caller is acting as, in the casing the tenant is stored under.</summary>
    public const string TenantId = "TenantId";

    /// <summary>
    /// Emitted once per tenant the caller is an approved member of, so that authorization can
    /// restore the full set rather than assuming it is just <see cref="TenantId"/>.
    /// </summary>
    public const string AuthorizedTenantId = "AuthorizedTenantId";

    /// <summary>
    /// The caller's conversation participant id. Carried as a claim because it can differ from
    /// <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>, which holds the canonical
    /// `provider|subject` id on the JWT paths.
    /// </summary>
    public const string ParticipantId = "ParticipantId";

    /// <summary>
    /// The account's stored email when present. Restored so participant ownership can accept an
    /// email-shaped participant id without re-reading the user document.
    /// </summary>
    public const string Email = "Email";

    /// <summary>
    /// The raw provider subject from the JWT. Restored so clients that pass <c>sub</c> as
    /// participant id remain authorized when <see cref="ParticipantId"/> prefers email.
    /// </summary>
    public const string ProviderSubject = "ProviderSubject";

    /// <summary>
    /// The kind of credential the caller authenticated with, as a <see cref="CredentialType"/> name.
    /// Carried as a claim so authorization handlers can restore what authentication determined
    /// instead of assuming a credential kind.
    /// </summary>
    public const string UserType = "UserType";

    /// <summary>
    /// Reads the credential kind from <paramref name="principal"/>, falling back to
    /// <see cref="CredentialType.UserToken"/> when the claim is missing or unrecognised. That
    /// fallback is the conservative one: a caller treated as a token holder is held to the
    /// participant ownership rule, whereas an API key is exempt from it.
    /// </summary>
    public static CredentialType ReadUserType(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(UserType)?.Value;

        if (Enum.TryParse<CredentialType>(raw, out var parsed))
        {
            return parsed;
        }

        return CredentialType.UserToken;
    }
}
