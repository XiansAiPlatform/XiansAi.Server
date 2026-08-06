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
}
