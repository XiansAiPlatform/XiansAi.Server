using Features.Mcp.Tools;
using ModelContextProtocol;
using Moq;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class DiscoveryToolsTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IPermissionsService> _permissions = new();
    private DiscoveryTools Tools() => new(_tenant.Object, _agents.Object, _activations.Object, _permissions.Object);

    public DiscoveryToolsTests() => _tenant.SetupGet(x => x.TenantId).Returns("tenant");

    [Fact]
    public void ListsOnlyAuthenticatedTenant() => Assert.Equal(["tenant"], Tools().ListTenants());

    [Fact]
    public async Task DiscoveryRejectsOtherTenantBeforeRepositoryAccess()
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().ListAgents("other"));
        await Assert.ThrowsAsync<McpException>(() => Tools().ListActivations("other", "agent"));
        _agents.VerifyNoOtherCalls();
        _activations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivationDiscoveryRequiresAgentReadAccess()
    {
        _permissions.Setup(x => x.HasReadPermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Tools().ListActivations("tenant", "agent"));
        _activations.VerifyNoOtherCalls();
    }
}
