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
