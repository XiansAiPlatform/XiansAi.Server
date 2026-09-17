using System.ComponentModel;
using System.Text.Json;
using Features.WebApi.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Shared.Auth;
using Shared.Models.Schedule;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Temporalio.Client.Schedules;

namespace Features.Mcp.Tools;

[McpServerToolType]
public sealed class ScheduleTools(
    ITenantContext tenantContext,
    IPermissionsService permissions,
    IAgentRepository agents,
    IFlowDefinitionRepository definitions,
    IActivationRepository activations,
    ITemporalGatewayFactory temporal,
    IScheduleService schedules)
{
    private static string Prefix(McpTarget target) => $"{target.TenantId}:{target.AgentName}:{target.ActivationName}:";

    private static bool BelongsToTarget(ScheduleModel schedule, McpTarget target) =>
        schedule.Id.StartsWith(Prefix(target), StringComparison.Ordinal) &&
        schedule.Id.LastIndexOf(':') == Prefix(target).Length - 1 &&
        schedule.TenantId == target.TenantId && schedule.AgentName == target.AgentName &&
        schedule.Metadata.TryGetValue("idPostfix", out var activation) &&
        activation is string name && name == target.ActivationName;

    private async Task<Agent> AuthorizeAsync(McpTarget target, bool write)
    {
        McpTarget.Validate(target);
        if (target.TenantId != tenantContext.TenantId)
            throw new McpException("Tenant access denied.");
        if (new[] { target.TenantId, target.AgentName, target.ActivationName }.Any(value => value.Contains(':')))
            throw new McpException("Schedule target identifiers cannot contain a colon.");
        var permission = write
            ? await permissions.HasWritePermission(target.AgentName)
            : await permissions.HasReadPermission(target.AgentName);
        if (!permission.IsSuccess || !permission.Data)
            throw new McpException("Agent access denied.");
        var agentTask = agents.GetByNameAsync(target.AgentName, tenantContext.TenantId,
            tenantContext.LoggedInUser, tenantContext.UserRoles);
        var activationTask = activations.GetByNameAndAgentAsync(tenantContext.TenantId,
            target.AgentName, target.ActivationName);
        await Task.WhenAll(agentTask, activationTask);
        var agent = await agentTask;
        var activation = await activationTask;
        if (agent is null || activation is null)
            throw new McpException("Agent or activation not found.");
        return agent;
    }

    private static T Result<T>(ServiceResult<T> result)
    {
        if (!result.IsSuccess) throw new McpException(result.ErrorMessage ?? "Schedule operation failed.");
        return result.Data!;
    }

    private async Task AuthorizeScheduleAsync(McpTarget target, string scheduleId)
    {
        await AuthorizeAsync(target, true);
        if (!scheduleId.StartsWith(Prefix(target), StringComparison.Ordinal))
            throw new McpException("Schedule does not belong to this activation. Use an exact ID from list_schedules.");
        var schedule = Result(await schedules.GetScheduleByIdAsync(scheduleId));
        if (!BelongsToTarget(schedule, target))
            throw new McpException("Schedule access denied.");
    }

    [McpServerTool(Name = "list_schedules", ReadOnly = true)]
    [Description("List schedules in this activation. Use returned exact IDs for modifications. Page is zero-based.")]
    public async Task<List<ScheduleModel>> ListSchedules(McpTarget target, int page = 0)
    {
        await AuthorizeAsync(target, false);
        if (page < 0) throw new McpException("Page must be non-negative.");
        return Result(await schedules.GetSchedulesAsync(new ScheduleFilterRequest
        {
            AgentName = target.AgentName, SearchTerm = Prefix(target), PageSize = 100, PageToken = page.ToString()
        })).Where(schedule => BelongsToTarget(schedule, target)).ToList();
    }

    [McpServerTool(Name = "list_workflows", ReadOnly = true)]
    [Description("Discover this agent's registered workflow types and ordered input parameters before creating a schedule. Registration does not guarantee an agent worker is currently running.")]
    public async Task<object[]> ListWorkflows(McpTarget target)
    {
        var agent = await AuthorizeAsync(target, false);
        var workflows = await definitions.GetByNameAsync(agent.Name, tenantContext.TenantId);
        return (workflows ?? []).DistinctBy(flow => flow.WorkflowType)
            .Select(flow => (object)new { flow.WorkflowType, flow.Summary, Parameters = flow.ParameterDefinitions }).ToArray();
    }

    [McpServerTool(Name = "create_schedule")]
    [Description("Schedule a registered workflow. Arguments are its ordered JSON input values. Output delivery is determined by the workflow, not MCP. Duplicate names fail; list first.")]
    public async Task<string> CreateSchedule(McpTarget target, string scheduleName, string workflowType, JsonElement[] arguments,
        string cron, string timezone = "UTC", string? description = null)
    {
        var agent = await AuthorizeAsync(target, true);
        if (string.IsNullOrWhiteSpace(scheduleName) || scheduleName.Contains(':'))
            throw new McpException("Schedule name is required and cannot contain a colon.");
        var workflows = await definitions.GetByNameAsync(agent.Name, tenantContext.TenantId);
        if (workflows?.Any(flow => flow.WorkflowType == workflowType) != true)
            throw new McpException("Workflow must be registered on this agent.");
        var options = new NewWorkflowOptions(agent.Name, agent.SystemScoped, workflowType,
            target.ActivationName, tenantContext);
        options.Memo = new Dictionary<string, object>(options.Memo!) { ["description"] = description ?? scheduleName };
        options.IdConflictPolicy = Temporalio.Api.Enums.V1.WorkflowIdConflictPolicy.Unspecified;
        var action = ScheduleActionStartWorkflow.Create(workflowType, arguments.Cast<object>().ToArray(), options);
        var spec = Timing(cron, timezone);
        var client = await temporal.GetClientAsync(agent.Name);
        var id = Prefix(target) + scheduleName;
        await client.CreateScheduleAsync(id, new Schedule(action, spec),
            new ScheduleOptions { TypedSearchAttributes = options.TypedSearchAttributes });
        return id;
    }

    private static ScheduleSpec Timing(string cron, string timezone)
    {
        if (string.IsNullOrWhiteSpace(cron)) throw new McpException("Cron expression is required.");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _)) throw new McpException("Invalid timezone.");
        return new ScheduleSpec { CronExpressions = [cron], TimeZoneName = timezone };
    }

    [McpServerTool(Name = "update_schedule_timing")]
    [Description("Change cron timing of an existing schedule using its exact ID. Preserves workflow arguments and pause state.")]
    public async Task<bool> UpdateScheduleTiming(McpTarget target, string scheduleId, string cron, string timezone = "UTC")
    {
        await AuthorizeScheduleAsync(target, scheduleId);
        var spec = Timing(cron, timezone);
        var client = await temporal.GetClientAsync(target.AgentName);
        await client.GetScheduleHandle(scheduleId).UpdateAsync(update =>
            new ScheduleUpdate(update.Description.Schedule with { Spec = spec }));
        return true;
    }

    [McpServerTool(Name = "delete_schedule", Destructive = true)]
    [Description("Delete a schedule using its exact ID from list_schedules.")]
    public async Task<bool> DeleteSchedule(McpTarget target, string scheduleId)
    {
        await AuthorizeScheduleAsync(target, scheduleId);
        return Result(await schedules.DeleteScheduleByIdAsync(scheduleId));
    }

    [McpServerTool(Name = "pause_schedule")]
    [Description("Pause a schedule using its exact ID.")]
    public async Task<bool> PauseSchedule(McpTarget target, string scheduleId)
    {
        await AuthorizeScheduleAsync(target, scheduleId);
        return Result(await schedules.PauseScheduleAsync(scheduleId));
    }

    [McpServerTool(Name = "resume_schedule")]
    [Description("Resume a paused schedule using its exact ID.")]
    public async Task<bool> ResumeSchedule(McpTarget target, string scheduleId)
    {
        await AuthorizeScheduleAsync(target, scheduleId);
        return Result(await schedules.ResumeScheduleAsync(scheduleId));
    }
}
