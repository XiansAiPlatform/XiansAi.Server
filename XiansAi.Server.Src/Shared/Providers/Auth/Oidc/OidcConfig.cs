namespace Shared.Providers.Auth.Oidc;

public class OidcConfig
{
    public string ProviderName { get; set; } = "Generic OIDC";
    public string Authority { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public string Audience { get; set; } = string.Empty;
    public string Scopes { get; set; } = "openid profile email";
    public bool RequireSignedTokens { get; set; } = true;
    public string[] AcceptedAlgorithms { get; set; } = new[] { "RS256" };
    public bool RequireHttpsMetadata { get; set; } = true;
    public OidcClaimMappings ClaimMappings { get; set; } = new();
    /// <summary>
    /// Optional. Comma-separated list of Azure AD / Entra ID group Object IDs used for automatic SysAdmin promotion.
    /// When set, every login checks whether the authenticated user's token contains any of these group IDs
    /// in the "groups" or "roles" claims. If any match is found, the user's IsSysAdmin flag is set to true
    /// in the database; if none match, it is set to false — keeping SysAdmin status automatically in sync
    /// with Azure AD group membership on every login.
    /// Leave empty or unset to disable this feature entirely.
    /// Environment variable: Oidc__AdminGroupIds=&lt;guid1&gt;,&lt;guid2&gt;
    /// </summary>
    public string? AdminGroupIds { get; set; }
}

public class OidcClaimMappings
{
    public string UserIdClaim { get; set; } = "sub";
    public string EmailClaim { get; set; } = "email";
    public string NameClaim { get; set; } = "name";
}

