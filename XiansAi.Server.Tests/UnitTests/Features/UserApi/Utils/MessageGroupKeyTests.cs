using Features.UserApi.Utils;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Features.UserApi.Utils;

public class MessageGroupKeyTests
{
    [Fact]
    public void ForParticipant_IsStableForTheSameIdentifiers()
    {
        var first = MessageGroupKey.ForParticipant("acme:Agent:Flow", "alice@acme.com", "acme");
        var second = MessageGroupKey.ForParticipant("acme:Agent:Flow", "alice@acme.com", "acme");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ForParticipant_DoesNotCollideWhenPartBoundariesShift()
    {
        // Plain concatenation made these two identical.
        var first = MessageGroupKey.ForParticipant("acme:Agent:Flow", "ab", "acme");
        var second = MessageGroupKey.ForParticipant("acme:Agent:Flowa", "b", "acme");

        Assert.NotEqual(first, second);
    }

    [Theory]
    // A participant id containing the separator must not be able to shift the part
    // boundaries and impersonate another combination of identifiers.
    [InlineData("acme:Agent:Flow", "auth0|123", "acme", "acme:Agent:Flow", "auth0", "123|acme")]
    [InlineData("acme:Agent:Flow", "a|b", "acme", "acme:Agent:Flow", "a", "b|acme")]
    // The escape character itself must be escaped, otherwise a literal backslash
    // followed by a separator collides with an escaped separator.
    [InlineData("acme:Agent:Flow", "a\\", "b|acme", "acme:Agent:Flow", "a\\b", "acme")]
    public void ForParticipant_DoesNotCollideWhenPartsContainSeparators(
        string firstWorkflow,
        string firstParticipant,
        string firstTenant,
        string secondWorkflow,
        string secondParticipant,
        string secondTenant)
    {
        var first = MessageGroupKey.ForParticipant(firstWorkflow, firstParticipant, firstTenant);
        var second = MessageGroupKey.ForParticipant(secondWorkflow, secondParticipant, secondTenant);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ForParticipant_LeavesOrdinaryIdentifiersUnescaped()
    {
        var key = MessageGroupKey.ForParticipant("acme:Agent:Flow", "alice@acme.com", "acme");

        Assert.Equal("participant|acme:Agent:Flow|alice@acme.com|acme", key);
    }

    [Fact]
    public void ForParticipant_IsCaseSensitiveSoCallersMustNormalize()
    {
        // Stored messages carry a lowercased participant id, so subscribers have to
        // lowercase before building the key. Guards the ChatHub / SSE call sites.
        Assert.NotEqual(
            MessageGroupKey.ForParticipant("acme:Agent:Flow", "Alice@acme.com", "acme"),
            MessageGroupKey.ForParticipant("acme:Agent:Flow", "alice@acme.com", "acme"));
    }

    [Fact]
    public void ForTenant_DoesNotCollideWhenPartsContainSeparators()
    {
        Assert.NotEqual(
            MessageGroupKey.ForTenant("acme:Agent:Flow|x", "acme"),
            MessageGroupKey.ForTenant("acme:Agent:Flow", "x|acme"));
    }

    [Fact]
    public void ForParticipant_NeverMatchesTenantKey()
    {
        var tenantKey = MessageGroupKey.ForTenant("acme:Agent:Flow", "acme");

        Assert.NotEqual(tenantKey, MessageGroupKey.ForParticipant("acme:Agent:Flow", "", "acme"));
        Assert.NotEqual(tenantKey, MessageGroupKey.ForParticipant("acme:Agent:Flow", "tenant", "acme"));
        Assert.NotEqual(tenantKey, MessageGroupKey.ForParticipant("acme:Agent:Flow", null, "acme"));
    }

    [Fact]
    public void ForParticipant_TreatsMissingPartsAsEmptyInsteadOfThrowing()
    {
        var key = MessageGroupKey.ForParticipant(null, null, null);

        Assert.NotNull(key);
        Assert.NotEqual(MessageGroupKey.ForParticipant("acme:Agent:Flow", "alice@acme.com", "acme"), key);
    }

    [Fact]
    public void ForTenant_DistinguishesWorkflowAndTenant()
    {
        Assert.NotEqual(
            MessageGroupKey.ForTenant("acme:Agent:Flow", "acme"),
            MessageGroupKey.ForTenant("acme:Agent:Flow", "other"));
    }
}
