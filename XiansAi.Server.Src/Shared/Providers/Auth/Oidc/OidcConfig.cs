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
}

public class OidcClaimMappings
{
    public string UserIdClaim { get; set; } = "sub";
    public string EmailClaim { get; set; } = "email";
    public string NameClaim { get; set; } = "name";
}

