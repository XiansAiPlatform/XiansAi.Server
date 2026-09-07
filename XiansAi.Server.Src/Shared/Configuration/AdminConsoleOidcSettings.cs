namespace Shared.Configuration;

/// <summary>
/// Configuration for the "admin-console" pseudo-tenant's OIDC providers — the identity providers
/// any AdminApi client's own human callers log in through (agent-studio today; possibly other
/// admin clients later, each with their own IdP). Deliberately a dictionary, not named properties
/// per provider: adding a provider — a third one for agent-studio, or a first one for some future
/// client — is then a config change only. See AdminConsoleOidcSeeder, which turns this into the
/// same TenantOidcRules shape a real tenant's config uses.
/// </summary>
public class AdminConsoleOidcSettings
{
    public const string SectionName = "AdminConsoleOidc";

    /// <summary>
    /// Off by default would mean silently not seeding; true by default means a deployment that
    /// simply never configured any provider seeds nothing (see AdminConsoleOidcSeeder), so this
    /// only matters for deliberately disabling seeding without removing every provider entry.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Keyed by an arbitrary provider name chosen by whoever configures it (e.g. "google",
    /// "azure-ad", "okta") — the name is never interpreted, it only has to be unique.</summary>
    public Dictionary<string, AdminConsoleOidcProviderSettings>? Providers { get; set; }
}

public class AdminConsoleOidcProviderSettings
{
    /// <summary>Required. The IdP's OIDC authority (JWKS/discovery is fetched from here).</summary>
    public string? Authority { get; set; }

    /// <summary>Optional; defaults to Authority. Set separately only when a provider's token
    /// issuer claim differs from its discovery authority.</summary>
    public string? Issuer { get; set; }

    /// <summary>Required. The client/application ID this provider issued for the caller's login —
    /// becomes the expected audience, so a token minted for a different app is rejected.</summary>
    public string? ClientId { get; set; }

    /// <summary>Optional; left unset by default. Only set this if the provider actually puts a
    /// verifiable scope/scp claim in its ID tokens (most don't - scope is normally an access-token
    /// concept) - setting it turns on a check that a real ID token would otherwise always fail.</summary>
    public string? Scope { get; set; }
}
