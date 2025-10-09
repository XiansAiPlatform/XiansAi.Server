## Generic OIDC in XiansAi.Server (Web API)

This guide explains how the server validates JWT access tokens issued by any OpenID Connect (OIDC) provider. It’s written for beginners and focuses on what you need to configure and how the request flow works.

### What Generic OIDC Means Here
- The server does not perform an interactive sign‑in. Your frontend/app signs the user in with the provider and obtains a JWT access token.
- The server only validates that JWT on every request. Therefore, client ID/secret and redirect URIs are handled on the client side, not by this server.

### Where to Configure
Set the provider type and the generic OIDC settings in `appsettings.json`:

```12:44:C:\99xProjectDir\xians.ai\New folder\XiansAi.Server\XiansAi.Server.Src\appsettings.json
// Supported Provider values: "Auth0", "AzureB2C", "Keycloak", "Oidc"
"AuthProvider": {
  "Provider": "Oidc"
},

"Oidc": {
  "ProviderName": "AzureB2C",            // free-form, for logs only
  "Authority": "https://<your-auth-domain>/", // base URL; used for discovery
  "Issuer": "https://<your-issuer>/",     // optional; defaults to Authority
  "Audience": "api://your-api-id",        // must match the token's audience
  "Scopes": "openid,profile,email",       // informational here
  "RequireSignedTokens": true,
  "AcceptedAlgorithms": ["RS256"],
  "RequireHttpsMetadata": true,
  "ClaimMappings": {
    "UserIdClaim": "sub"                  // or "oid", etc. based on your IdP
  }
}
```

Key fields the server uses:
- **Authority/Issuer**: used to fetch discovery metadata (`.well-known/openid-configuration`) and to validate the issuer.
- **Audience**: must match the `aud` in the access token presented to the API.
- **AcceptedAlgorithms** and **RequireSignedTokens**: restrict acceptable signing algorithms.
- **ClaimMappings.UserIdClaim**: which claim to treat as the unique user id (e.g. `sub`, `oid`).

### Request Authentication Flow
1. Frontend acquires an access token from your IdP (using client credentials on the frontend or SPA flow).
2. Frontend calls this API with `Authorization: Bearer <access-token>`.
3. Server’s JWT bearer handler validates the token using your `Oidc` config:
   - Loads discovery metadata from `Authority/.well-known/openid-configuration`.
   - Verifies signature, issuer, audience, lifetime, and allowed algorithms.
4. After validation, the server extracts the user id using `ClaimMappings.UserIdClaim`, with fallbacks to common claims (e.g., `sub`).
5. If you pass `X-Tenant-Id` header, the server loads role claims for that user+tenant (via `IRoleCacheService`). If not, it assigns a default `TenantUser` role.

Code references:

```8:21:C:\99xProjectDir\xians.ai\New folder\XiansAi.Server\XiansAi.Server.Src\Features\WebApi\Auth\AuthConfigurationExtensions.cs
public static WebApplicationBuilder AddWebApiAuth(this WebApplicationBuilder builder)
{
    builder.Services.AddAuthentication()
    .AddJwtBearer("JWT", options =>
    {
        var serviceProvider = builder.Services.BuildServiceProvider();
        var authProviderFactory = serviceProvider.GetRequiredService<IAuthProviderFactory>();
        var authProvider = authProviderFactory.GetProvider();
        authProvider.ConfigureJwtBearer(options, builder.Configuration);
    });
```

```37:53:C:\99xProjectDir\xians.ai\New folder\XiansAi.Server\XiansAi.Server.Src\Shared\Providers\Auth\Oidc\OidcProvider.cs
public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
{
    options.Authority = _config.Authority;
    options.RequireHttpsMetadata = _config.RequireHttpsMetadata;
    options.TokenValidationParameters.ValidIssuer = _config.Issuer ?? _config.Authority;
    options.TokenValidationParameters.ValidAudience = _config.Audience;
    options.TokenValidationParameters.RequireSignedTokens = _config.RequireSignedTokens;
    options.TokenValidationParameters.ValidAlgorithms = _config.AcceptedAlgorithms;
    options.MapInboundClaims = false;
    // OnTokenValidated → set user id and role claims
}
```

### User Profile Handling
- Generic OIDC does not call a provider management API. The server returns minimal info based on token claims and your own app data.
- Roles come from your own store (`IRoleCacheService`), keyed by user id and `X-Tenant-Id`.

```187:201:C:\99xProjectDir\xians.ai\New folder\XiansAi.Server\XiansAi.Server.Src\Shared\Providers\Auth\Oidc\OidcProvider.cs
public Task<UserInfo> GetUserInfo(string userId)
{
    // No provider mgmt API — return minimal info
    return Task.FromResult(new UserInfo { UserId = userId, ... });
}
```

### Differences vs Provider-Specific Modes
- **Auth0**: Same JWT validation, plus optional user profile and app metadata (e.g., tenants) via Auth0 Management API.
  - See `Shared/Providers/Auth/Auth0/Auth0Provider.cs`.
- **Azure Entra ID/B2C**: Same JWT validation, can fetch user info via Microsoft Graph if configured.
  - See `Shared/Providers/Auth/AzureB2C/AzureB2CProvider.cs`.
- **Generic OIDC**: No provider management API usage. Pure JWT validation + claim mapping + your app’s role loading.

### Quick Checklist
- Set `AuthProvider:Provider` to `Oidc`.
- Configure `Oidc.Authority`, `Issuer` (optional), `Audience`, `AcceptedAlgorithms`.
- Map `ClaimMappings.UserIdClaim` to your IdP’s unique id claim.
- Ensure frontend requests include `Authorization: Bearer <token>`.
- Optionally include `X-Tenant-Id` to load tenant‑specific roles.

### Troubleshooting Tips
- 401 with "Invalid issuer/audience": check `Issuer`/`Authority` and `Audience` values against the token.
- 401 with "Signing algorithm not allowed": ensure `AcceptedAlgorithms` includes the IdP’s signing alg (e.g., `RS256`).
- No roles assigned: pass `X-Tenant-Id` and verify your role store configuration.


