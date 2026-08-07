using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Auth;

namespace Tests.UnitTests.Shared.Auth;

public class IdentityAutoLinkPolicyTests
{
    private static IdentityAutoLinkPolicy BuildPolicy(params string[] trustedProviders)
    {
        var settings = trustedProviders
            .Select((authority, index) =>
                new KeyValuePair<string, string?>($"Auth:AutoLinkTrustedProviders:{index}", authority))
            .ToList();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new IdentityAutoLinkPolicy(configuration, NullLogger<IdentityAutoLinkPolicy>.Instance);
    }

    private static IdentityAutoLinkPolicy BuildPolicyVouchingFor(params string[] authorities)
    {
        var settings = authorities
            .Select((authority, index) => new KeyValuePair<string, string?>(
                $"Auth:AutoLinkProvidersWithoutVerifiedEmailClaim:{index}", authority))
            .ToList();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new IdentityAutoLinkPolicy(configuration, NullLogger<IdentityAutoLinkPolicy>.Instance);
    }

    [Fact]
    public void Google_IsTrustedWithoutConfiguration()
    {
        // Google states that a signed-in holder controls the address it puts in `email`, so a match
        // means something without an operator having to say so.
        Assert.True(BuildPolicy().IsTrusted("https://accounts.google.com"));
    }

    [Fact]
    public void NothingElse_IsTrustedWithoutConfiguration()
    {
        var policy = BuildPolicy();

        Assert.False(policy.IsTrusted("https://login.microsoftonline.com/common/v2.0"));
        Assert.False(policy.IsTrusted("https://login.example.com"));
    }

    [Theory]
    [InlineData("https://accounts.google.com/")]
    [InlineData("https://Accounts.Google.com")]
    public void TrustMatching_IgnoresTrailingSlashAndCase(string authority)
    {
        // These are the same provider, and the rest of the codebase already compares authorities
        // this way; differing here would silently refuse a sign-in that should link.
        Assert.True(BuildPolicy().IsTrusted(authority));
    }

    [Fact]
    public void ConfiguringTrustedProviders_ReplacesTheDefault()
    {
        var policy = BuildPolicy("https://login.example.com");

        Assert.True(policy.IsTrusted("https://login.example.com"));
        Assert.False(policy.IsTrusted("https://accounts.google.com"));
    }

    [Fact]
    public void ASingleEntraTenant_MayBeTrusted()
    {
        // One directory, whose administrators the operator has decided to rely on.
        var authority = "https://login.microsoftonline.com/8f7d2c11-0000-0000-0000-abcdef123456/v2.0";

        Assert.Null(IdentityAutoLinkPolicy.DescribeUntrustableAuthority(authority));
        Assert.True(BuildPolicy(authority).IsTrusted(authority));
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/common/v2.0")]
    [InlineData("https://login.microsoftonline.com/organizations/v2.0")]
    [InlineData("https://login.microsoftonline.com/consumers/v2.0")]
    public void AMultiTenantMicrosoftEndpoint_IsRefusedEvenWhenConfigured(string authority)
    {
        // Any Microsoft directory can issue these, and its administrators choose their users' email
        // addresses, so an address match identifies nobody. Accepting one would let anyone who can
        // create a directory sign in as any account whose address they know.
        Assert.NotNull(IdentityAutoLinkPolicy.DescribeUntrustableAuthority(authority));
        Assert.False(BuildPolicy(authority).IsTrusted(authority));
    }

    [Theory]
    [InlineData("http://accounts.google.com")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void AnUnusableAuthority_IsRefused(string authority)
    {
        Assert.NotNull(IdentityAutoLinkPolicy.DescribeUntrustableAuthority(authority));
        Assert.False(BuildPolicy(authority).IsTrusted(authority));
    }

    [Fact]
    public void ARefusedEntry_DoesNotDiscardTheRestOfTheConfiguration()
    {
        var policy = BuildPolicy("https://login.microsoftonline.com/common/v2.0", "https://login.example.com");

        Assert.True(policy.IsTrusted("https://login.example.com"));
    }

    [Fact]
    public void NoAuthority_IsNeverTrusted()
    {
        var policy = BuildPolicy();

        Assert.False(policy.IsTrusted(null));
        Assert.False(policy.IsTrusted(""));
    }

    [Fact]
    public void NoProvider_IsVouchedForWithoutConfiguration()
    {
        // Standing in for a provider's verification claim is never the default. An operator has to
        // say they know how their directory admits addresses.
        var policy = BuildPolicy();

        Assert.False(policy.VouchesForUnverifiedEmail("https://accounts.google.com"));
        Assert.False(policy.VouchesForUnverifiedEmail("https://contoso.b2clogin.com/contoso.onmicrosoft.com/v2.0"));
    }

    [Fact]
    public void MerelyTrustingAProvider_DoesNotVouchForItsUnverifiedEmails()
    {
        // The two lists say different things: trusting Google means believing what it verifies, not
        // accepting an address it declined to verify.
        var policy = BuildPolicy("https://accounts.google.com");

        Assert.True(policy.IsTrusted("https://accounts.google.com"));
        Assert.False(policy.VouchesForUnverifiedEmail("https://accounts.google.com"));
    }

    [Fact]
    public void AVouchedProvider_IsTrustedWithoutBeingNamedTwice()
    {
        // Vouching is the stronger statement, so requiring the authority in both lists would only
        // create a way to configure something that silently does nothing.
        var authority = "https://contoso.b2clogin.com/contoso.onmicrosoft.com/v2.0";
        var policy = BuildPolicyVouchingFor(authority);

        Assert.True(policy.VouchesForUnverifiedEmail(authority));
        Assert.True(policy.IsTrusted(authority));
    }

    [Fact]
    public void AVouchedProvider_DoesNotDisplaceTheTrustedDefault()
    {
        var policy = BuildPolicyVouchingFor("https://contoso.b2clogin.com/contoso.onmicrosoft.com/v2.0");

        Assert.True(policy.IsTrusted("https://accounts.google.com"));
    }

    [Fact]
    public void AMultiTenantMicrosoftEndpoint_CannotBeVouchedForEither()
    {
        // The weaker list refuses these, so the stronger one must too — otherwise it would be a way
        // around the check rather than an extension of it.
        var authority = "https://login.microsoftonline.com/common/v2.0";
        var policy = BuildPolicyVouchingFor(authority);

        Assert.False(policy.VouchesForUnverifiedEmail(authority));
        Assert.False(policy.IsTrusted(authority));
    }

    [Fact]
    public void NoAuthority_IsNeverVouchedFor()
    {
        var policy = BuildPolicyVouchingFor("https://contoso.b2clogin.com/contoso.onmicrosoft.com/v2.0");

        Assert.False(policy.VouchesForUnverifiedEmail(null));
        Assert.False(policy.VouchesForUnverifiedEmail(""));
    }
}
