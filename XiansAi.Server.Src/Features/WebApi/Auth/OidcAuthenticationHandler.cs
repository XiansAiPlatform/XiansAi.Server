using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Utils;

namespace Features.WebApi.Auth;

/// <summary>
/// Custom authentication handler for Generic OIDC authentication in WebAPI.
/// Extracts JWT tokens from Authorization header and validates them using DynamicOidcValidator.
/// </summary>
public class OidcAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// WebAPI has no per-tenant OIDC configuration, so it validates against a single static
    /// provider registered under this pseudo-tenant.
    /// </summary>
    private const string WebApiPseudoTenant = "webapi";

    private readonly IDynamicOidcValidator _oidcValidator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OidcAuthenticationHandler> _logger;

    public OidcAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDynamicOidcValidator oidcValidator,
        ITenantContext tenantContext)
        : base(options, logger, encoder)
    {
        _oidcValidator = oidcValidator;
        _tenantContext = tenantContext;
        _logger = logger.CreateLogger<OidcAuthenticationHandler>();
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            // Check for Authorization header
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                _logger.LogDebug("Missing Authorization header");
                return AuthenticateResult.NoResult();
            }

            var authHeader = Request.Headers["Authorization"].ToString();
            
            // Safely extract Bearer token
            var (tokenExtracted, token) = AuthorizationHeaderHelper.ExtractBearerToken(authHeader);
            
            if (!tokenExtracted || token == null)
            {
                _logger.LogDebug("Invalid Authorization header format or empty token");
                return AuthenticateResult.Fail("Invalid Authorization header format. Expected 'Bearer <token>'");
            }

            _logger.LogDebug("Validating JWT token using Generic OIDC");
            var validation = await _oidcValidator.ValidateAsync(WebApiPseudoTenant, token);
            var canonicalUserId = validation.CanonicalUserId;

            if (!validation.Success)
            {
                _logger.LogWarning("OIDC token validation failed: {Error}", LogSanitizer.Sanitize(validation.Error));
                return AuthenticateResult.Fail("Invalid credentials");
            }

            if (string.IsNullOrWhiteSpace(canonicalUserId))
            {
                _logger.LogWarning("Token validation succeeded but no user identifier was extracted");
                return AuthenticateResult.Fail("Invalid credentials");
            }

            // The validator establishes identity only, so the request's tenant context is populated
            // here. AuthRequirementHandler re-derives the user and the tenants they may act as
            // during authorization; the bearer token is what has to survive from this point, since
            // it is forwarded to agents that call back on the caller's behalf.
            _tenantContext.LoggedInUser = validation.ProviderUserId!;
            _tenantContext.UserType = UserType.UserToken;
            _tenantContext.Authorization = token;

            // Build claims principal
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, canonicalUserId),
                new Claim("userId", canonicalUserId),
                new Claim("authMethod", "oidc")
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            _logger.LogInformation("User authenticated successfully via Generic OIDC: UserId={UserId}", canonicalUserId);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during OIDC authentication");
            return AuthenticateResult.Fail("Authentication failed due to internal error");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers["WWW-Authenticate"] = "Bearer";
        
        _logger.LogDebug("Authentication challenge: 401 Unauthorized");
        
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        
        _logger.LogDebug("Authorization failed: 403 Forbidden");
        
        return Task.CompletedTask;
    }
}

