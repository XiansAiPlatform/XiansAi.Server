using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.AdminApi.Auth;
using Shared.Models.Schedule;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for managing the schedules of a particular agent.
/// These are administrative operations that reuse the WebApi <see cref="IScheduleService"/>,
/// scoped to a single tenant/agent via the route.
/// All endpoints are under /api/v{version}/admin/tenants/{tenantId}/agents/{agentName}/schedules.
/// </summary>
public static class AdminScheduleEndpoints
{
    /// <summary>
    /// Maps all AdminApi schedule management endpoints.
    /// </summary>
    public static void MapAdminScheduleEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var scheduleGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agents/{agentName}/schedules")
            .WithTags("AdminAPI - Schedules")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        // List all schedules for the agent (with optional filtering)
        scheduleGroup.MapGet("", async (
            string tenantId,
            string agentName,
            [FromServices] IScheduleService scheduleService,
            [FromQuery] string? workflowType = null,
            [FromQuery] ScheduleStatus? status = null,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? pageToken = null,
            [FromQuery] string? searchTerm = null) =>
        {
            var request = new ScheduleFilterRequest
            {
                AgentName = agentName,
                WorkflowType = workflowType,
                Status = status,
                PageSize = pageSize,
                PageToken = pageToken,
                SearchTerm = searchTerm
            };

            var result = await scheduleService.GetSchedulesAsync(request);
            return result.ToHttpResult();
        })
        .Produces<List<ScheduleModel>>()
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("AdminGetAgentSchedules")
        .WithSummary("Get all schedules for an agent")
        .WithDescription("Retrieves all schedules for the specified agent with optional filtering by workflow type, status, and search term.")
        ;

        // Get a specific schedule by ID
        scheduleGroup.MapGet("/by-id", async Task<IResult> (
            string tenantId,
            string agentName,
            [FromQuery] string scheduleId,
            [FromServices] IScheduleService scheduleService) =>
        {
            if (string.IsNullOrWhiteSpace(scheduleId))
            {
                return Results.BadRequest(new { message = "scheduleId is required" });
            }

            var result = await scheduleService.GetScheduleByIdAsync(scheduleId);
            return EnsureScheduleBelongsToAgent(result, agentName) ?? result.ToHttpResult();
        })
        .Produces<ScheduleModel>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminGetAgentScheduleById")
        .WithSummary("Get a schedule by ID")
        .WithDescription("Retrieves detailed information about a specific schedule belonging to the agent. Provide scheduleId as a query parameter.")
        ;

        // Get upcoming runs for a schedule
        scheduleGroup.MapGet("/upcoming-runs", async Task<IResult> (
            string tenantId,
            string agentName,
            [FromQuery] string scheduleId,
            [FromServices] IScheduleService scheduleService,
            [FromQuery] int count = 10) =>
        {
            if (string.IsNullOrWhiteSpace(scheduleId))
            {
                return Results.BadRequest(new { message = "scheduleId is required" });
            }

            var ownershipError = await EnsureScheduleOwnedByAgent(scheduleService, scheduleId, agentName);
            if (ownershipError != null)
            {
                return ownershipError;
            }

            var result = await scheduleService.GetUpcomingRunsAsync(scheduleId, count);
            return result.ToHttpResult();
        })
        .Produces<List<ScheduleRunModel>>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminGetAgentScheduleUpcomingRuns")
        .WithSummary("Get upcoming runs for a schedule")
        .WithDescription("Retrieves the next scheduled executions for a specific schedule belonging to the agent. Provide scheduleId as a query parameter.")
        ;

        // Get execution history for a schedule
        scheduleGroup.MapGet("/history", async Task<IResult> (
            string tenantId,
            string agentName,
            [FromQuery] string scheduleId,
            [FromServices] IScheduleService scheduleService,
            [FromQuery] int count = 50) =>
        {
            if (string.IsNullOrWhiteSpace(scheduleId))
            {
                return Results.BadRequest(new { message = "scheduleId is required" });
            }

            var ownershipError = await EnsureScheduleOwnedByAgent(scheduleService, scheduleId, agentName);
            if (ownershipError != null)
            {
                return ownershipError;
            }

            var result = await scheduleService.GetScheduleHistoryAsync(scheduleId, count);
            return result.ToHttpResult();
        })
        .Produces<List<ScheduleRunModel>>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminGetAgentScheduleHistory")
        .WithSummary("Get schedule execution history")
        .WithDescription("Retrieves the execution history for a specific schedule belonging to the agent. Provide scheduleId as a query parameter.")
        ;

        // Pause (suspend) a specific schedule
        scheduleGroup.MapPost("/pause", async Task<IResult> (
            string tenantId,
            string agentName,
            [FromQuery] string scheduleId,
            [FromServices] IScheduleService scheduleService,
            [FromQuery] string? note = null) =>
        {
            if (string.IsNullOrWhiteSpace(scheduleId))
            {
                return Results.BadRequest(new { message = "scheduleId is required" });
            }

            var ownershipError = await EnsureScheduleOwnedByAgent(scheduleService, scheduleId, agentName);
            if (ownershipError != null)
            {
                return ownershipError;
            }

            var result = await scheduleService.PauseScheduleAsync(scheduleId, note);
            return result.ToHttpResult();
        })
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminPauseAgentSchedule")
        .WithSummary("Pause a schedule")
        .WithDescription("Pauses (suspends) a specific schedule belonging to the agent. Provide scheduleId as a query parameter and an optional note.")
        ;

        // Resume (unpause) a specific schedule
        scheduleGroup.MapPost("/resume", async Task<IResult> (
            string tenantId,
            string agentName,
            [FromQuery] string scheduleId,
            [FromServices] IScheduleService scheduleService,
            [FromQuery] string? note = null) =>
        {
            if (string.IsNullOrWhiteSpace(scheduleId))
            {
                return Results.BadRequest(new { message = "scheduleId is required" });
            }

            var ownershipError = await EnsureScheduleOwnedByAgent(scheduleService, scheduleId, agentName);
            if (ownershipError != null)
            {
                return ownershipError;
            }

            var result = await scheduleService.ResumeScheduleAsync(scheduleId, note);
            return result.ToHttpResult();
        })
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminResumeAgentSchedule")
        .WithSummary("Resume a schedule")
        .WithDescription("Resumes (unpauses) a specific schedule belonging to the agent. Provide scheduleId as a query parameter and an optional note.")
        ;

        // Delete a specific schedule by ID
        scheduleGroup.MapDelete("/by-id", async Task<IResult> (
            string tenantId,
            string agentName,
            [FromQuery] string scheduleId,
            [FromServices] IScheduleService scheduleService) =>
        {
            if (string.IsNullOrWhiteSpace(scheduleId))
            {
                return Results.BadRequest(new { message = "scheduleId is required" });
            }

            var ownershipError = await EnsureScheduleOwnedByAgent(scheduleService, scheduleId, agentName);
            if (ownershipError != null)
            {
                return ownershipError;
            }

            var result = await scheduleService.DeleteScheduleByIdAsync(scheduleId);
            return result.ToHttpResult();
        })
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminDeleteAgentScheduleById")
        .WithSummary("Delete a schedule by ID")
        .WithDescription("Deletes a specific schedule belonging to the agent. Provide scheduleId as a query parameter.")
        ;

        // Delete all schedules for the agent
        scheduleGroup.MapDelete("", async (
            string tenantId,
            string agentName,
            [FromServices] IScheduleService scheduleService) =>
        {
            var result = await scheduleService.DeleteAllSchedulesByAgentAsync(agentName);
            return result.ToHttpResult();
        })
        .Produces<ScheduleDeleteResult>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("AdminDeleteAllAgentSchedules")
        .WithSummary("Delete all schedules for an agent")
        .WithDescription("Deletes all schedules associated with the specified agent. This operation cannot be undone.")
        ;
    }

    /// <summary>
    /// Verifies the schedule identified by <paramref name="scheduleId"/> belongs to
    /// <paramref name="agentName"/>. The schedule service scopes by tenant (validated against the
    /// resolved context by <see cref="TenantRouteScopeFilter"/>) but not by agent, so this check
    /// keeps the agent-scoped routes from operating on another agent's schedule.
    /// Returns null when the schedule is owned by the agent; otherwise the error result to return.
    /// </summary>
    private static async Task<IResult?> EnsureScheduleOwnedByAgent(
        IScheduleService scheduleService,
        string scheduleId,
        string agentName)
    {
        var result = await scheduleService.GetScheduleByIdAsync(scheduleId);
        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        return EnsureScheduleBelongsToAgent(result, agentName);
    }

    /// <summary>
    /// Returns a 404 result when the (successful) schedule lookup does not belong to the agent,
    /// otherwise null. A 404 (rather than 403) avoids disclosing schedules of other agents.
    /// </summary>
    private static IResult? EnsureScheduleBelongsToAgent(ServiceResult<ScheduleModel> result, string agentName)
    {
        if (result.IsSuccess &&
            !string.Equals(result.Data?.AgentName, agentName, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound(new { message = "Schedule not found for the specified agent" });
        }

        return null;
    }
}
