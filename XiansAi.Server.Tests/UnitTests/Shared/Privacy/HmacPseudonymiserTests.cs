using Shared.Configuration;
using Shared.Services.Privacy;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

public class HmacPseudonymiserTests
{
    private static HmacPseudonymiser Create(string key) => new(new PrivacyOptions { PseudonymKey = key });

    [Fact]
    public void SameValue_GivesSameHash()
    {
        var pseudonymiser = Create("key-one-0123456789abcdef0123456789");

        Assert.Equal(pseudonymiser.Pseudonymise("alice@example.com"), pseudonymiser.Pseudonymise("alice@example.com"));
    }

    [Fact]
    public void Hash_IgnoresCaseAndSurroundingSpace()
    {
        var pseudonymiser = Create("key-one-0123456789abcdef0123456789");

        Assert.Equal(pseudonymiser.Pseudonymise("alice@example.com"), pseudonymiser.Pseudonymise("  Alice@Example.COM "));
    }

    [Fact]
    public void DifferentKeys_GiveDifferentHashes()
    {
        var first = Create("key-one-0123456789abcdef0123456789").Pseudonymise("alice@example.com");
        var second = Create("key-two-0123456789abcdef0123456789").Pseudonymise("alice@example.com");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Hash_IsLowercaseHexOfSha256Length_AndHidesTheValue()
    {
        var hash = Create("key-one-0123456789abcdef0123456789").Pseudonymise("alice@example.com");

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain("alice", hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    public void KeyShorterThanTheMinimum_Throws(string? key)
    {
        Assert.Throws<InvalidOperationException>(() => new HmacPseudonymiser(new PrivacyOptions { PseudonymKey = key }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankValue_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => Create("key-one-0123456789abcdef0123456789").Pseudonymise(value));
    }
}
