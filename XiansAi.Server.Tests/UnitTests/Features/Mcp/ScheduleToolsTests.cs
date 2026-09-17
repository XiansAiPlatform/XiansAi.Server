using Features.Mcp.Tools;
using Features.WebApi.Services;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using Moq;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Shared.Data.Models;
using Shared.Models.Schedule;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class ScheduleToolsTests
{
    private McpTarget _target = new("tenant", "agent", "activation");
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IPermissionsService> _permissions = new();
    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IScheduleService> _schedules = new();
    private readonly Mock<IFlowDefinitionRepository> _definitions = new();

    private ScheduleTools Tools() => new(
        _tenant.Object, _permissions.Object, _agents.Object, _definitions.Object, _activations.Object,
        Mock.Of<ITemporalGatewayFactory>(), _schedules.Object);

    public ScheduleToolsTests()
    {
        _tenant.SetupGet(x => x.TenantId).Returns("tenant");
    }

    [Fact]
    public async Task ListRejectsDifferentTenant()
    {
        _target = _target with { TenantId = "other" };
        await Assert.ThrowsAsync<McpException>(() => Tools().ListSchedules(_target));
        _permissions.VerifyNoOtherCalls();
        _schedules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteRequiresWritePermission()
    {
        _permissions.Setup(x => x.HasWritePermission("agent"))
            .ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteSchedule(_target, "tenant:agent:activation:test"));
        _schedules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListRequiresReadPermission()
    {
        _permissions.Setup(x => x.HasReadPermission("agent"))
            .ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Tools().ListSchedules(_target));
        _schedules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WorkflowDiscoveryRequiresReadPermission()
    {
        _permissions.Setup(x => x.HasReadPermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Tools().ListWorkflows(_target));
        _definitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WorkflowDiscoveryReturnsOrderedParametersWithoutSource()
    {
        AllowAccess();
        var flow = new FlowDefinition
        {
            Id = "id", Agent = "agent", WorkflowType = "agent:scheduled", Hash = "abc", CreatedBy = "user",
            ActivityDefinitions = [], Source = "private source",
            ParameterDefinitions = [new ParameterDefinition { Name = "request", Type = "ScheduledPromptRequest" }]
        };
        _definitions.Setup(x => x.GetByNameAsync("agent", "tenant")).ReturnsAsync([flow, flow]);
        var result = System.Text.Json.JsonSerializer.SerializeToElement(await Tools().ListWorkflows(_target));
        Assert.Equal(1, result.GetArrayLength());
        Assert.Equal("agent:scheduled", result[0].GetProperty("WorkflowType").GetString());
        Assert.Equal("request", result[0].GetProperty("Parameters")[0].GetProperty("Name").GetString());
        Assert.False(result[0].TryGetProperty("Source", out _));
    }

    private void AllowAccess()
    {
        _permissions.Setup(x => x.HasWritePermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(true));
        _permissions.Setup(x => x.HasReadPermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(true));
        _tenant.SetupGet(x => x.LoggedInUser).Returns("user");
        _tenant.SetupGet(x => x.UserRoles).Returns([]);
        _agents.Setup(x => x.GetByNameAsync("agent", "tenant", "user", It.IsAny<string[]>()))
            .ReturnsAsync(new Agent { Id = "id", Name = "agent", Tenant = "tenant", CreatedBy = "user" });
        _activations.Setup(x => x.GetByNameAndAgentAsync("tenant", "agent", "activation"))
            .ReturnsAsync(new AgentActivation { Id = "id", Name = "activation", AgentName = "agent", TenantId = "tenant", CreatedBy = "user" });
    }

    [Fact]
    public async Task DeleteRejectsAnotherActivationBeforeLookup()
    {
        AllowAccess();
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteSchedule(_target, "tenant:agent:other:test"));
        _schedules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteRejectsMismatchedMemo()
    {
        AllowAccess();
        const string id = "tenant:agent:activation:test";
        _schedules.Setup(x => x.GetScheduleByIdAsync(id)).ReturnsAsync(ServiceResult<ScheduleModel>.Success(
            new ScheduleModel { Id = id, TenantId = "other", AgentName = "agent", WorkflowType = "workflow", ScheduleSpec = "cron" }));
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteSchedule(_target, id));
        _schedules.Verify(x => x.DeleteScheduleByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateRejectsUnregisteredWorkflow()
    {
        AllowAccess();
        await Assert.ThrowsAsync<McpException>(() => Tools().CreateSchedule(_target, "test", "unregistered", [], "0 9 * * *"));
    }
}
