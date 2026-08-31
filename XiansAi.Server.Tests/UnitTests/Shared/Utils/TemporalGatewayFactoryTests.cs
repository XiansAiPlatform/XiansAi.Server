using Moq;
using Shared.Auth;
using Shared.Utils.Temporal;
using Temporalio.Client;

namespace XiansAi.Server.Tests.UnitTests.Shared.Utils;

public class TemporalGatewayFactoryTests
{
    private const string TenantId = "acme";

    [Fact]
    public async Task GetClientsForAgentAsync_UsesSingleOriginRoutedClient_WhenAgentIsProvided()
    {
        var client = Mock.Of<ITemporalClient>();
        var gateway = new Mock<ITemporalGatewayService>();
        gateway
            .Setup(g => g.GetClientAsync(TenantId, "Bot"))
            .ReturnsAsync(client);

        var factory = CreateFactory(gateway.Object);

        var clients = new List<ITemporalClient>();
        await foreach (var resolved in factory.GetClientsForAgentAsync("Bot"))
        {
            clients.Add(resolved);
        }

        Assert.Single(clients);
        Assert.Same(client, clients[0]);
        gateway.Verify(g => g.GetClientsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetClientsForAgentAsync_FansOutAcrossTenantClusters_WhenAgentIsMissing()
    {
        var client = Mock.Of<ITemporalClient>();
        var gateway = new Mock<ITemporalGatewayService>();
        gateway
            .Setup(g => g.GetClientsAsync(TenantId))
            .Returns(SingleClient(client));

        var factory = CreateFactory(gateway.Object);

        var clients = new List<ITemporalClient>();
        await foreach (var resolved in factory.GetClientsForAgentAsync(null))
        {
            clients.Add(resolved);
        }

        Assert.Single(clients);
        Assert.Same(client, clients[0]);
        gateway.Verify(g => g.GetClientAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    private static TemporalGatewayFactory CreateFactory(ITemporalGatewayService gateway)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(TenantId);
        return new TemporalGatewayFactory(gateway, tenantContext.Object);
    }

    private static async IAsyncEnumerable<ITemporalClient> SingleClient(ITemporalClient client)
    {
        yield return client;
        await Task.Yield();
    }
}
