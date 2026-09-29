using System.ComponentModel;
using System.Text.Json;
using Features.WebApi.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Shared.Auth;
using Shared.Data.Models;
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

    private static bool BelongsToTarget(ScheduleModel schedule, McpTarget target, string prefix) =>
        schedule.Id.StartsWith(prefix, StringComparison.Ordinal) &&
        schedule.Id.LastIndexOf(':') == prefix.Length - 1 &&
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
        if ((int)result.StatusCode >= 500) throw new McpException("Schedule operation failed.");
        if (!result.IsSuccess) throw new McpException(result.ErrorMessage ?? "Schedule operation failed.");
        return result.Data!;
    }

    private async Task AuthorizeScheduleAsync(McpTarget target, string scheduleId)
    {
        await AuthorizeAsync(target, true);
        var prefix = Prefix(target);
        if (!scheduleId.StartsWith(prefix, StringComparison.Ordinal))
            throw new McpException("Schedule does not belong to this activation. Use an exact ID from list_schedules.");
        var schedule = Result(await schedules.GetScheduleByIdAsync(scheduleId));
        if (!BelongsToTarget(schedule, target, prefix))
            throw new McpException("Schedule access denied.");
    }

    [McpServerTool(Name = "list_schedules", ReadOnly = true)]
    [Description("List schedules in this activation with status, timing, workflow type, and stored workflow inputs. Use returned exact IDs for modifications. Pages are zero-based with up to 100 results; continue until a page is empty.")]
    public async Task<List<ScheduleModel>> ListSchedules(
        McpTarget target,
        [Description("Zero-based page number; each page contains up to 100 schedules.")] int page = 0)
    {
        await AuthorizeAsync(target, false);
        if (page < 0) throw new McpException("Page must be non-negative.");
        var prefix = Prefix(target);
        return Result(await schedules.GetSchedulesAsync(new ScheduleFilterRequest
        {
            AgentName = target.AgentName, SearchTerm = prefix, PageSize = 100, PageToken = page.ToString()
        })).Where(schedule => BelongsToTarget(schedule, target, prefix)).ToList();
    }

    [McpServerTool(Name = "list_workflows", ReadOnly = true)]
    [Description("List the agent's registered workflow types and ordered input parameters; workflows define executable behavior, not schedules or past runs. Workflow definitions belong to the agent, while schedules and saved data belong to an activation. Use the exact workflow type and ordered arguments with create_schedule for the target activation. Registration does not guarantee an agent worker is currently running.")]
    public async Task<object[]> ListWorkflows(McpTarget target)
    {
        var agent = await AuthorizeAsync(target, false);
        var workflows = await definitions.GetByNameAsync(agent.Name, tenantContext.TenantId);
        return (workflows ?? []).DistinctBy(flow => flow.WorkflowType)
            .Select(flow => (object)new { flow.WorkflowType, flow.Summary, Parameters = flow.ParameterDefinitions }).ToArray();
    }

    [McpServerTool(Name = "create_schedule")]
    [Description("Schedule a registered workflow. First use list_workflows for its exact type and ordered JSON inputs, and list_schedules to avoid duplicate names. Schedule names cannot contain colons. Use an explicit user timezone instead of assuming UTC when unknown. Output delivery is determined by the workflow. Success creates the schedule but does not guarantee an agent worker is running or that execution will succeed. Use the returned exact ID for later operations.")]
    public async Task<string> CreateSchedule(
        McpTarget target,
        [Description("Unique friendly name within the activation; cannot contain a colon.")] string scheduleName,
        [Description("Exact registered workflow type returned by list_workflows.")] string workflowType,
        [Description("Ordered JSON values matching the workflow parameters returned by list_workflows.")] JsonElement[] arguments,
        [Description("Temporal cron expression describing when the workflow starts.")] string cron,
        [Description("IANA or system timezone ID; use the user's explicit timezone rather than assuming UTC.")] string timezone = "UTC",
        [Description("Optional human-readable purpose shown with the schedule.")] string? description = null)
    {
        var agent = await AuthorizeAsync(target, true);
        if (string.IsNullOrWhiteSpace(scheduleName) || scheduleName.Contains(':'))
            throw new McpException("Schedule name is required and cannot contain a colon.");
        var workflows = await definitions.GetByNameAsync(agent.Name, tenantContext.TenantId);
        var workflow = workflows?.FirstOrDefault(flow => flow.WorkflowType == workflowType);
        if (workflow is null)
            throw new McpException("Workflow must be registered on this agent.");
        ValidateArguments(workflow.ParameterDefinitions, arguments);
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

    private static void ValidateArguments(IReadOnlyCollection<ParameterDefinition> parameters, JsonElement[] arguments)
    {
        var required = parameters.Count(parameter => !parameter.Optional);
        if (arguments.Length < required || arguments.Length > parameters.Count)
            throw new McpException($"Workflow requires {required} to {parameters.Count} ordered arguments.");
    }

    [McpServerTool(Name = "update_schedule_timing")]
    [Description("Change future timing of an existing schedule using its exact ID from list_schedules. Preserves workflow arguments and pause state and does not execute immediately. Use an explicit user timezone instead of assuming UTC when unknown.")]
    public async Task<bool> UpdateScheduleTiming(
        McpTarget target,
        [Description("Exact schedule ID returned by list_schedules.")] string scheduleId,
        [Description("Replacement Temporal cron expression for future starts.")] string cron,
        [Description("IANA or system timezone ID; use the user's explicit timezone rather than assuming UTC.")] string timezone = "UTC")
    {
        await AuthorizeScheduleAsync(target, scheduleId);
        var spec = Timing(cron, timezone);
        var client = await temporal.GetClientAsync(target.AgentName);
        await client.GetScheduleHandle(scheduleId).UpdateAsync(update =>
            new ScheduleUpdate(update.Description.Schedule with { Spec = spec }));
        return true;
    }

    [McpServerTool(Name = "delete_schedule", Destructive = true)]
    [Description("Delete a schedule using its exact ID from list_schedules. Set confirmed=true only after the user explicitly approves deletion.")]
    public async Task<bool> DeleteSchedule(
        McpTarget target,
        [Description("Exact schedule ID returned by list_schedules.")] string scheduleId,
        [Description("Set true only after explicit user approval for permanent deletion.")] bool confirmed = false)
    {
        await AuthorizeScheduleAsync(target, scheduleId);
        if (!confirmed) throw new McpException("Explicit user confirmation is required before permanent deletion.");
        return Result(await schedules.DeleteScheduleByIdAsync(scheduleId));
    }

    [McpServerTool(Name = "pause_schedule")]
    [Description("Pause future starts of a schedule using its exact ID. The schedule and stored workflow inputs are preserved.")]
    public async Task<bool> PauseSchedule(
        McpTarget target,
        [Description("Exact schedule ID returned by list_schedules.")] string scheduleId)
    {
        await AuthorizeScheduleAsync(target, scheduleId);
        return Result(await schedules.PauseScheduleAsync(scheduleId));
    }

    [McpServerTool(Name = "resume_schedule")]
    [Description("Resume future starts of a paused schedule using its exact ID. This does not execute it immediately.")]
    public async Task<bool> ResumeSchedule(
        McpTarget target,
        [Description("Exact schedule ID returned by list_schedules.")] string scheduleId)
    {
        await AuthorizeScheduleAsync(target, scheduleId);
        return Result(await schedules.ResumeScheduleAsync(scheduleId));
    }
}
