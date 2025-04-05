using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XiansAi.Server.Tests.TestUtils;

public class TestAuthenticationOptions : AuthenticationSchemeOptions
{
}

public class TestAuthHandler : AuthenticationHandler<TestAuthenticationOptions>
{
#pragma warning disable CS0618 // Type or member is obsolete
    public TestAuthHandler(
        IOptionsMonitor<TestAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock) 
        : base(options, logger, encoder, clock)
    {
    }
#pragma warning restore CS0618 // Type or member is obsolete

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] {
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim("tenant_id", "test-tenant"),
            new Claim("client_id", "test-client"),
            new Claim("thumbprint", "test-thumbprint")
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
} 