using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;
using Shared.Models.Schedule;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this WebApplication app)
    {
        // Map schedule endpoints with common attributes
        var schedulesGroup = app.MapGroup("/api/client/schedules")
            .WithTags("WebAPI - Schedules")
            .RequiresValidTenant()
            .RequireAuthorization();

        // Get all schedules with optional filtering
        schedulesGroup.MapGet("/", async (
            [FromServices] IScheduleService scheduleService,
            [FromQuery] string? agentName = null,
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
        .WithName("GetSchedules")
        .WithSummary("Get all schedules")
        .WithDescription("Retrieves all schedules with optional filtering by agent name, workflow type, status, and search term")
        .WithOpenApi();

        // Delete all schedules for an agent (more specific route - must come before /{scheduleId})
        schedulesGroup.MapDelete("/agent/{agentName}", async (
            string agentName,
            [FromServices] IScheduleService scheduleService) =>
        {
            var result = await scheduleService.DeleteAllSchedulesByAgentAsync(agentName);
            return result.ToHttpResult();
        })
        .WithName("DeleteAllSchedulesByAgent")
        .WithSummary("Delete all schedules for an agent")
        .WithDescription("Deletes all schedules associated with a specific agent. Requires write permission for the agent.")
        .WithOpenApi();

        // Get upcoming runs for a schedule (more specific route - must come before /{scheduleId})
        schedulesGroup.MapGet("/{scheduleId}/upcoming-runs", async (
            string scheduleId,
            [FromServices] IScheduleService scheduleService,
            [FromQuery] int count = 10) =>
        {
            var result = await scheduleService.GetUpcomingRunsAsync(scheduleId, count);
            return result.ToHttpResult();
        })
        .WithName("GetUpcomingRuns")
        .WithSummary("Get upcoming runs for a schedule")
        .WithDescription("Retrieves the next scheduled executions for a specific schedule")
        .WithOpenApi();

        // Get execution history for a schedule (more specific route - must come before /{scheduleId})
        schedulesGroup.MapGet("/{scheduleId}/history", async (
            string scheduleId,
            [FromServices] IScheduleService scheduleService,
            [FromQuery] int count = 50) =>
        {
            var result = await scheduleService.GetScheduleHistoryAsync(scheduleId, count);
            return result.ToHttpResult();
        })
        .WithName("GetScheduleHistory")
        .WithSummary("Get schedule execution history")
        .WithDescription("Retrieves the execution history for a specific schedule")
        .WithOpenApi();

        // Get schedule by ID (less specific route)
        schedulesGroup.MapGet("/{scheduleId}", async (
            string scheduleId,
            [FromServices] IScheduleService scheduleService) =>
        {
            var result = await scheduleService.GetScheduleByIdAsync(scheduleId);
            return result.ToHttpResult();
        })
        .WithName("GetScheduleById")
        .WithSummary("Get schedule by ID")
        .WithDescription("Retrieves detailed information about a specific schedule")
        .WithOpenApi();

        // Delete a specific schedule by ID (less specific route)
        schedulesGroup.MapDelete("/{scheduleId}", async (
            string scheduleId,
            [FromServices] IScheduleService scheduleService) =>
        {
            var result = await scheduleService.DeleteScheduleByIdAsync(scheduleId);
            return result.ToHttpResult();
        })
        .WithName("DeleteScheduleById")
        .WithSummary("Delete a schedule by ID")
        .WithDescription("Deletes a specific schedule. Requires write permission for the associated agent.")
        .WithOpenApi();
    }
}