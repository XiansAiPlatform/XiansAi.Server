using Features.Mcp.Tools;
using ModelContextProtocol;
using Moq;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Data.Models;
using Microsoft.Extensions.Logging;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class DiscoveryToolsTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IPermissionsService> _permissions = new();
    private readonly Mock<IAgentPermissionRepository> _agentPermissions = new();
    private DiscoveryTools Tools() => new(_tenant.Object, _agents.Object, _activations.Object, _permissions.Object, _agentPermissions.Object);

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

    [Theory]
    [InlineData("", "allowed,template")]
    [InlineData("TenantAdmin", "allowed,denied,template")]
    [InlineData("SysAdmin", "allowed,denied,template,private-template")]
    public async Task AgentDiscoveryFiltersLoadedBatchUsingExistingPolicy(string role, string expected)
    {
        _tenant.SetupGet(x => x.LoggedInUser).Returns("user");
        _tenant.SetupGet(x => x.UserRoles).Returns(role.Split(',', StringSplitOptions.RemoveEmptyEntries));
        Agent Agent(string name, string? tenant, bool readable)
        {
            var agent = new Agent { Id = name, Name = name, Tenant = tenant, CreatedBy = "owner" };
            if (readable) agent.GrantReadAccess("user");
            return agent;
        }
        _agents.Setup(x => x.GetAgentsWithPermissionAsync("user", "tenant"))
            .ReturnsAsync([Agent("allowed", "tenant", true), Agent("denied", "tenant", false)]);
        _agents.Setup(x => x.GetSystemScopedAgentsWithDefinitionsAsync(true)).ReturnsAsync([
            new AgentWithDefinitions { Agent = Agent("template", null, true), Definitions = [] },
            new AgentWithDefinitions { Agent = Agent("private-template", null, false), Definitions = [] },
            new AgentWithDefinitions { Agent = Agent("allowed", null, true), Definitions = [] }
        ]);
        var policy = new AgentPermissionRepository(_agents.Object, Mock.Of<IFlowDefinitionRepository>(),
            Mock.Of<ILogger<AgentPermissionRepository>>(), _tenant.Object);
        var tools = new DiscoveryTools(_tenant.Object, _agents.Object, _activations.Object, _permissions.Object, policy);
        var result = System.Text.Json.JsonSerializer.SerializeToElement(await tools.ListAgents("tenant"));
        Assert.Equal(expected.Split(','), result.EnumerateArray().Select(item => item.GetProperty("AgentName").GetString()));
        _permissions.VerifyNoOtherCalls();
        _agents.Verify(x => x.GetByNameInternalAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
