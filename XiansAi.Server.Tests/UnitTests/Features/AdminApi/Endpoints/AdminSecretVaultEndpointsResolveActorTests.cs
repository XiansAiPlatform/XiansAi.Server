using Features.AdminApi.Endpoints;
using Moq;
using Shared.Auth;

namespace Tests.UnitTests.Features.AdminApi.Endpoints;

/// <summary>
/// CreatedBy/UpdatedBy on secrets come from the on-behalf-of participant, and fall back to
/// "system" only when no identity is available at all.
/// </summary>
public class AdminSecretVaultEndpointsResolveActorTests
{
    private static ITenantContext TenantContext(string? participantId)
    {
        var context = new Mock<ITenantContext>();
        context.Setup(x => x.ParticipantId).Returns(participantId!);
        return context.Object;
    }

    [Fact]
    public void ResolveActor_ReturnsParticipantId_WhenSet()
    {
        Assert.Equal("ui-user", AdminSecretVaultEndpoints.ResolveActor(TenantContext("ui-user")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveActor_FallsBackToSystem_WhenNoIdentityIsAvailable(string? participantId)
    {
        Assert.Equal("system", AdminSecretVaultEndpoints.ResolveActor(TenantContext(participantId)));
    }
}
