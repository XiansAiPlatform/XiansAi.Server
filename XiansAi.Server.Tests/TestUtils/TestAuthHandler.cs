using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Auth;

namespace Tests.TestUtils;

public class TestAuthenticationOptions : AuthenticationSchemeOptions
{
}

public class TestAuthHandler : AuthenticationHandler<TestAuthenticationOptions>
{
    private const string TestTenantId = "test-tenant";
    private const string TestUserId = "test-user";
    private readonly ITenantContext _tenantContext;

    public TestAuthHandler(
        IOptionsMonitor<TestAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITenantContext tenantContext)
        : base(options, logger, encoder)
    {
        _tenantContext = tenantContext;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            // Check for API key in Authorization header
            string? apiKey = null;
            if (Request.Headers.ContainsKey("Authorization"))
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    apiKey = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            // Check for tenant ID in header
            var tenantId = Request.Headers["X-Tenant-Id"].ToString();
            var hasTenantHeader = !string.IsNullOrWhiteSpace(tenantId);
            if (!hasTenantHeader)
            {
                tenantId = TestTenantId;
            }

            // CertificateAuthenticationHandler writes ITenantContext. Tests remap Agent API
            // to this scheme; only Agent paths get cert-equivalent tenant binding so Web API
            // tests keep the factory authorized-tenant list.
            if (hasTenantHeader && Request.Path.StartsWithSegments("/api/agent"))
            {
                _tenantContext.TenantId = tenantId;
                _tenantContext.AuthorizedTenantIds = [tenantId];
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.LoggedInUser))
            {
                _tenantContext.LoggedInUser = TestUserId;
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.ParticipantId))
            {
                _tenantContext.ParticipantId = TestUserId;
            }

            // Create claims for the authenticated user
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, TestUserId),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim(ClaimTypes.Email, "test@example.com"),
                new Claim("tenant_id", tenantId),
                new Claim("tenant", tenantId), // Add both formats for compatibility
                new Claim("sub", TestUserId),
                new Claim("scope", "read:all write:all"),
                new Claim("permissions", "read:all"),
                new Claim("permissions", "write:all"),
                new Claim(ClaimTypes.Role, "SysAdmin"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim(ClaimTypes.Role, "TenantUser"),
                new Claim("authorized_tenants", TestTenantId),
                new Claim("authorized_tenants", "99x.io"),
                new Claim("organization", tenantId)
            };

            // Add API key claim if present
            if (!string.IsNullOrEmpty(apiKey))
            {
                claims.Add(new Claim("api_key", apiKey));
            }

            var identity = new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in test authentication handler");
            return Task.FromResult(AuthenticateResult.Fail("Authentication failed"));
        }
    }
}
