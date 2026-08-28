using Microsoft.Extensions.Configuration;
using Moq;
using Shared.Auth;
using Shared.Repositories;

namespace Tests.UnitTests.Shared.Auth;

public class TenantContextParticipantIdTests
{
    private static TenantContext BuildContext(string loggedInUser) =>
        new(new ConfigurationBuilder().Build(), Mock.Of<ITenantTemporalConfigRepository>())
        {
            TenantId = "acme",
            LoggedInUser = loggedInUser,
            UserRoles = []
        };

    [Fact]
    public void ParticipantId_FallsBackToTheLoggedInUser_WhenNotSetExplicitly()
    {
        var context = BuildContext("abc123");

        Assert.Equal("abc123", context.ParticipantId);
    }

    [Fact]
    public void ParticipantId_TracksTheLoggedInUser_WhileItRemainsUnset()
    {
        var context = BuildContext("abc123");

        context.LoggedInUser = "keycloak|abc123";

        Assert.Equal("keycloak|abc123", context.ParticipantId);
    }

    [Fact]
    public void ParticipantId_IsIndependentOfTheLoggedInUser_OnceSetExplicitly()
    {
        var context = BuildContext("keycloak|abc123");

        context.ParticipantId = "abc123";
        context.LoggedInUser = "keycloak|abc123";

        Assert.Equal("abc123", context.ParticipantId);
        Assert.Equal("keycloak|abc123", context.LoggedInUser);
    }
}
