using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Auth;

/// <summary>
/// Covers the rules a tenant's OIDC configuration is not allowed to weaken. The authority checks
/// matter most: the authority is supplied by a tenant administrator and the server fetches it
/// unauthenticated, so anything it accepts is something the server can be made to request.
/// </summary>
public class OidcValidationPolicyTests
{
    private static OidcValidationPolicy CreatePolicy(
        string? environmentName = null,
        Dictionary<string, string?>? settings = null)
    {
        environmentName ??= Environments.Production;

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        return new OidcValidationPolicy(
            configuration,
            environment.Object,
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            NullLogger<OidcValidationPolicy>.Instance);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/tenant/v2.0")]
    [InlineData("https://accounts.google.com")]
    [InlineData("https://keycloak.example.com/realms/xians")]
    public void PublicHttpsAuthorityIsAcceptedInProduction(string authority)
    {
        Assert.Null(CreatePolicy().DescribeUnsafeAuthority(authority));
    }

    [Theory]
    [InlineData("http://login.example.com")]              // plaintext, so keys can be swapped in transit
    [InlineData("https://localhost:8443/realms/xians")]   // names something inside the deployment
    [InlineData("https://127.0.0.1/realms/xians")]
    [InlineData("https://10.1.2.3/realms/xians")]
    [InlineData("https://172.16.0.5/realms/xians")]
    [InlineData("https://192.168.1.1/realms/xians")]
    [InlineData("https://169.254.169.254/latest/meta-data")] // cloud instance metadata
    [InlineData("https://[::1]/realms/xians")]
    [InlineData("https://[fd00::1]/realms/xians")]
    public void UnreachableOrPlaintextAuthorityIsRejectedInProduction(string authority)
    {
        Assert.NotNull(CreatePolicy().DescribeUnsafeAuthority(authority));
    }

    [Theory]
    [InlineData("http://localhost:8080/realms/xians")]
    [InlineData("https://127.0.0.1:8443/realms/xians")]
    public void LocalAuthorityIsAllowedOutsideProduction(string authority)
    {
        Assert.Null(CreatePolicy(Environments.Development).DescribeUnsafeAuthority(authority));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("login.example.com")]                     // no scheme, so not a fetchable address
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com")]
    public void AuthorityThatIsNotAnHttpUrlIsAlwaysRejected(string? authority)
    {
        Assert.NotNull(CreatePolicy(Environments.Development).DescribeUnsafeAuthority(authority));
    }

    [Fact]
    public void ProviderAlgorithmsTakePrecedenceOverDiscovery()
    {
        var allowed = OidcValidationPolicy.ResolveAllowedAlgorithms(
            new[] { "RS256" }, new[] { "RS256", "HS256" });

        Assert.Equal(new[] { "RS256" }, allowed);
    }

    [Fact]
    public void DiscoveryAlgorithmsAreUsedWhenProviderListsNone()
    {
        var allowed = OidcValidationPolicy.ResolveAllowedAlgorithms(null, new[] { "RS256", "ES256" });

        Assert.Equal(new[] { "RS256", "ES256" }, allowed);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    public void UnsignedAlgorithmIsStrippedFromAnyList(string none)
    {
        var allowed = OidcValidationPolicy.ResolveAllowedAlgorithms(new[] { "RS256", none }, null);

        Assert.Equal(new[] { "RS256" }, allowed);
    }

    [Fact]
    public void ListingOnlyUnsignedLeavesNoAllowedAlgorithms()
    {
        // Falling back to null lets the signing keys decide, which still cannot verify an unsigned
        // token. What must not happen is 'none' reaching the validation parameters as acceptable.
        Assert.Null(OidcValidationPolicy.ResolveAllowedAlgorithms(new[] { "none" }, null));
    }

    [Fact]
    public void NoAlgorithmsAnywhereImposesNoRestriction()
    {
        Assert.Null(OidcValidationPolicy.ResolveAllowedAlgorithms(null, null));
        Assert.Null(OidcValidationPolicy.ResolveAllowedAlgorithms(Array.Empty<string>(), null));
    }

    [Fact]
    public void AudienceAndSubjectEnforcementAreOffUnlessTurnedOn()
    {
        // Both change who can sign in, so an existing deployment has to opt in after clearing the
        // warnings rather than have them switched on by an upgrade.
        var policy = CreatePolicy();

        Assert.False(policy.RequireAudience);
        Assert.False(policy.RequireStandardSubjectClaim);
    }

    [Fact]
    public void EnforcementSwitchesAreReadFromConfiguration()
    {
        var policy = CreatePolicy(settings: new Dictionary<string, string?>
        {
            ["Auth:RequireOidcAudience"] = "true",
            ["Auth:StrictSubjectClaim"] = "true"
        });

        Assert.True(policy.RequireAudience);
        Assert.True(policy.RequireStandardSubjectClaim);
    }

    [Fact]
    public void RecurringWarningIsLoggedOncePerIntervalPerSubject()
    {
        var logger = new CountingLogger();
        var policy = new OidcValidationPolicy(
            new ConfigurationBuilder().Build(),
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Production),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            logger);

        for (var i = 0; i < 10; i++)
        {
            policy.WarnAboutConfiguration("audience:acme/entra", "misconfigured");
        }
        policy.WarnAboutConfiguration("audience:globex/entra", "misconfigured");

        // Once per tenant, not once per request: the condition recurs on every single request from
        // an affected tenant, and a log line per request would bury it.
        Assert.Equal(2, logger.WarningCount);
    }

    private sealed class CountingLogger : ILogger<OidcValidationPolicy>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
