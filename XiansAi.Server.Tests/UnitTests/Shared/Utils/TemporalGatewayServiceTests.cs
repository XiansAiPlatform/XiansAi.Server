using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Temporal;

namespace XiansAi.Server.Tests.UnitTests.Shared.Utils;

public class TemporalGatewayServiceTests
{
    private const string TenantId = "acme";

    [Fact]
    public async Task GetClientsAsync_AttemptsConnect_WhenCacheIsEmpty()
    {
        var agentRepository = new Mock<IAgentRepository>();
        var temporalConfigRepository = new Mock<ITenantTemporalConfigRepository>();

        agentRepository
            .Setup(r => r.GetDistinctOriginTenantsAsync(TenantId))
            .ReturnsAsync(new List<string>());
        temporalConfigRepository
            .Setup(r => r.GetAsync(TenantId))
            .ReturnsAsync((TenantTemporalConfig?)null);

        var service = CreateService(agentRepository.Object, temporalConfigRepository.Object);

        var enumerator = service.GetClientsAsync(TenantId).GetAsyncEnumerator();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator.MoveNextAsync());

        Assert.Contains("Temporal configuration", exception.Message);
        agentRepository.Verify(r => r.GetDistinctOriginTenantsAsync(TenantId), Times.Once);
        temporalConfigRepository.Verify(r => r.GetAsync(TenantId), Times.Once);
    }

    [Fact]
    public async Task GetClientsAsync_Throws_WhenTenantIdIsMissing()
    {
        var service = CreateService(
            Mock.Of<IAgentRepository>(),
            Mock.Of<ITenantTemporalConfigRepository>());

        var enumerator = service.GetClientsAsync(string.Empty).GetAsyncEnumerator();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task GetClientsAsync_ResolvesOriginTenants_BeforeConnecting()
    {
        var agentRepository = new Mock<IAgentRepository>();
        var temporalConfigRepository = new Mock<ITenantTemporalConfigRepository>();

        agentRepository
            .Setup(r => r.GetDistinctOriginTenantsAsync(TenantId))
            .ReturnsAsync(new List<string> { TenantId, "platform" });
        temporalConfigRepository
            .Setup(r => r.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((TenantTemporalConfig?)null);

        var service = CreateService(agentRepository.Object, temporalConfigRepository.Object);

        var enumerator = service.GetClientsAsync(TenantId).GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator.MoveNextAsync());

        agentRepository.Verify(r => r.GetDistinctOriginTenantsAsync(TenantId), Times.Once);
        temporalConfigRepository.Verify(r => r.GetAsync(TenantId), Times.Once);
        temporalConfigRepository.Verify(r => r.GetAsync("platform"), Times.Once);
    }

    [Fact]
    public async Task GetClientsAsync_StillAttemptsOriginTenant_WhenCallerTenantConfigIsMissing()
    {
        var agentRepository = new Mock<IAgentRepository>();
        var temporalConfigRepository = new Mock<ITenantTemporalConfigRepository>();

        agentRepository
            .Setup(r => r.GetDistinctOriginTenantsAsync(TenantId))
            .ReturnsAsync(new List<string> { "platform" });
        temporalConfigRepository
            .Setup(r => r.GetAsync(TenantId))
            .ReturnsAsync((TenantTemporalConfig?)null);
        temporalConfigRepository
            .Setup(r => r.GetAsync("platform"))
            .ReturnsAsync((TenantTemporalConfig?)null);

        var service = CreateService(agentRepository.Object, temporalConfigRepository.Object);

        var enumerator = service.GetClientsAsync(TenantId).GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator.MoveNextAsync());

        temporalConfigRepository.Verify(r => r.GetAsync(TenantId), Times.Once);
        temporalConfigRepository.Verify(r => r.GetAsync("platform"), Times.Once);
    }

    private static TemporalGatewayService CreateService(
        IAgentRepository agentRepository,
        ITenantTemporalConfigRepository temporalConfigRepository)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IAgentRepository)))
            .Returns(agentRepository);
        serviceProvider
            .Setup(sp => sp.GetService(typeof(ITenantTemporalConfigRepository)))
            .Returns(temporalConfigRepository);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new TemporalGatewayService(
            scopeFactory.Object,
            NullLogger<TemporalGatewayService>.Instance,
            new ConfigurationBuilder().Build());
    }
}
