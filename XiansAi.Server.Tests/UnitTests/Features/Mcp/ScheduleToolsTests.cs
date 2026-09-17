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
using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Client.Interceptors;
using Temporalio.Converters;
using System.Text.Json;

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
    private readonly Mock<ITemporalGatewayFactory> _temporal = new();
    private readonly Mock<ITemporalClient> _client = new();

    private ScheduleTools Tools() => new(
        _tenant.Object, _permissions.Object, _agents.Object, _definitions.Object, _activations.Object,
        _temporal.Object, _schedules.Object);

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

    private void AllowScheduleAccess()
    {
        AllowAccess();
        const string id = "tenant:agent:activation:test";
        _schedules.Setup(x => x.GetScheduleByIdAsync(id)).ReturnsAsync(ServiceResult<ScheduleModel>.Success(
            new ScheduleModel { Id = id, TenantId = "tenant", AgentName = "agent", WorkflowType = "agent:scheduled", ScheduleSpec = "cron" }));
        _temporal.Setup(x => x.GetClientAsync("agent")).ReturnsAsync(_client.Object);
    }

    private Task<bool> Mutate(string operation, string id) => operation switch
    {
        "update" => Tools().UpdateScheduleTiming(_target, id, "0 9 * * *"),
        "pause" => Tools().PauseSchedule(_target, id),
        "resume" => Tools().ResumeSchedule(_target, id),
        _ => throw new ArgumentException("Unknown operation")
    };

    [Theory]
    [InlineData("update")]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task MutationsRequireWriteAccess(string operation)
    {
        _permissions.Setup(x => x.HasWritePermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Mutate(operation, "tenant:agent:activation:test"));
        _schedules.VerifyNoOtherCalls();
        _temporal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("update")]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task MutationsRejectOtherActivation(string operation)
    {
        AllowAccess();
        await Assert.ThrowsAsync<McpException>(() => Mutate(operation, "tenant:agent:other:test"));
        _schedules.VerifyNoOtherCalls();
        _temporal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task PauseAndResumeUseExactAuthorizedId(string operation)
    {
        AllowScheduleAccess();
        const string id = "tenant:agent:activation:test";
        _schedules.Setup(x => x.PauseScheduleAsync(id)).ReturnsAsync(ServiceResult<bool>.Success(true));
        _schedules.Setup(x => x.ResumeScheduleAsync(id)).ReturnsAsync(ServiceResult<bool>.Success(true));
        Assert.True(await Mutate(operation, id));
        if (operation == "pause") _schedules.Verify(x => x.PauseScheduleAsync(id), Times.Once);
        else _schedules.Verify(x => x.ResumeScheduleAsync(id), Times.Once);
    }

    private void RegisterWorkflow()
    {
        AllowAccess();
        _definitions.Setup(x => x.GetByNameAsync("agent", "tenant")).ReturnsAsync([
            new FlowDefinition { Id = "id", Agent = "agent", WorkflowType = "agent:scheduled", Hash = "hash",
                CreatedBy = "user", ActivityDefinitions = [], ParameterDefinitions = [] }
        ]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid:name")]
    public async Task CreateRejectsInvalidName(string name)
    {
        RegisterWorkflow();
        await Assert.ThrowsAsync<McpException>(() => Tools().CreateSchedule(_target, name, "agent:scheduled", [], "0 9 * * *"));
        _temporal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "UTC")]
    [InlineData("0 9 * * *", "Invalid/Timezone")]
    public async Task CreateRejectsInvalidTimingBeforeConnecting(string cron, string timezone)
    {
        RegisterWorkflow();
        await Assert.ThrowsAsync<McpException>(() => Tools().CreateSchedule(_target, "test", "agent:scheduled", [], cron, timezone));
        _temporal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreatePreservesInputsAndTarget()
    {
        RegisterWorkflow();
        _temporal.Setup(x => x.GetClientAsync("agent")).ReturnsAsync(_client.Object);
        Schedule? created = null;
        _client.Setup(x => x.CreateScheduleAsync("tenant:agent:activation:test", It.IsAny<Schedule>(), It.IsAny<ScheduleOptions>()))
            .Callback<string, Schedule, ScheduleOptions>((_, schedule, _) => created = schedule)
            .ReturnsAsync(new ScheduleHandle(_client.Object, "tenant:agent:activation:test"));
        var arguments = new[] { JsonSerializer.SerializeToElement(new { Prompt = "task" }) };
        Assert.Equal("tenant:agent:activation:test", await Tools().CreateSchedule(_target, "test", "agent:scheduled", arguments, "0 9 * * *", "UTC", "description"));
        Assert.NotNull(created);
        Assert.Equal("UTC", created.Spec.TimeZoneName);
        Assert.Equal("0 9 * * *", Assert.Single(created.Spec.CronExpressions!));
        var action = Assert.IsType<ScheduleActionStartWorkflow>(created.Action);
        Assert.Equal("agent:scheduled", action.Workflow);
        Assert.Equal("tenant:agent:scheduled:activation", action.Options.Id);
        Assert.Equal(arguments[0].GetRawText(), Assert.IsType<JsonElement>(Assert.Single(action.Args)).GetRawText());
        Assert.Equal("description", action.Options.Memo!["description"]);
        Assert.Equal(Temporalio.Api.Enums.V1.WorkflowIdConflictPolicy.Unspecified, action.Options.IdConflictPolicy);
    }

    [Fact]
    public async Task CreatePropagatesTemporalCronValidation()
    {
        RegisterWorkflow();
        _temporal.Setup(x => x.GetClientAsync("agent")).ReturnsAsync(_client.Object);
        var error = new Temporalio.Exceptions.RpcException(
            Temporalio.Exceptions.RpcException.StatusCode.InvalidArgument, "Invalid cron expression", null);
        _client.Setup(x => x.CreateScheduleAsync(It.IsAny<string>(), It.IsAny<Schedule>(), It.IsAny<ScheduleOptions>()))
            .ThrowsAsync(error);
        var actual = await Assert.ThrowsAsync<Temporalio.Exceptions.RpcException>(() =>
            Tools().CreateSchedule(_target, "test", "agent:scheduled", [], "not a cron"));
        Assert.Same(error, actual);
    }

    [Theory]
    [InlineData("", "UTC")]
    [InlineData("0 9 * * *", "Invalid/Timezone")]
    public async Task TimingUpdateRejectsInvalidTimingBeforeConnecting(string cron, string timezone)
    {
        AllowScheduleAccess();
        await Assert.ThrowsAsync<McpException>(() => Tools().UpdateScheduleTiming(_target,
            "tenant:agent:activation:test", cron, timezone));
        _temporal.Verify(x => x.GetClientAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TimingUpdatePreservesActionAndState()
    {
        AllowScheduleAccess();
        var action = ScheduleActionStartWorkflow.Create("agent:scheduled", ["original input"], new WorkflowOptions("workflow", "queue"));
        var original = new Schedule(action, new ScheduleSpec { CronExpressions = ["0 8 * * *"] })
            { State = new ScheduleState { Paused = true } };
        var interceptor = new CapturingScheduleInterceptor(original);
        _client.SetupGet(x => x.OutboundInterceptor).Returns(interceptor);
        _client.Setup(x => x.GetScheduleHandle("tenant:agent:activation:test"))
            .Returns(new ScheduleHandle(_client.Object, "tenant:agent:activation:test"));
        Assert.True(await Tools().UpdateScheduleTiming(_target, "tenant:agent:activation:test", "0 9 * * *", "Asia/Colombo"));
        Assert.Equal("tenant:agent:activation:test", interceptor.Id);
        Assert.NotNull(interceptor.Updated);
        Assert.Same(action, interceptor.Updated.Action);
        Assert.Same(original.State, interceptor.Updated.State);
        Assert.True(interceptor.Updated.State.Paused);
        Assert.Equal("Asia/Colombo", interceptor.Updated.Spec.TimeZoneName);
        Assert.Equal("0 9 * * *", Assert.Single(interceptor.Updated.Spec.CronExpressions!));
    }

    private sealed class CapturingScheduleInterceptor(Schedule original) : ClientOutboundInterceptor(null!)
    {
        public Schedule? Updated { get; private set; }
        public string? Id { get; private set; }
        public override async Task UpdateScheduleAsync(UpdateScheduleInput input)
        {
            Id = input.Id;
            // The SDK exposes this snapshot only through an internal constructor.
            var description = (ScheduleDescription)Activator.CreateInstance(typeof(ScheduleDescription),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                [input.Id, original, new Temporalio.Api.WorkflowService.V1.DescribeScheduleResponse
                {
                    Info = new Temporalio.Api.Schedule.V1.ScheduleInfo
                        { CreateTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow) }
                }, DataConverter.Default], null)!;
            Updated = (await input.Updater(new ScheduleUpdateInput(description)))!.Schedule;
        }
    }
}
