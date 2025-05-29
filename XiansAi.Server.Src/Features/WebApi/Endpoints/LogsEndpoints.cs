using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class LogsEndpoints
{
    public static void MapLogsEndpoints(this WebApplication app)
    {
        // Map logs endpoints with common attributes
        var logsGroup = app.MapGroup("/api/client/logs")
            .WithTags("WebAPI - Logs")
            .RequiresValidTenant();

        logsGroup.MapGet("/{id}", async (
            string id,
            [FromServices] ILogsService service) =>
        {
            var result = await service.GetLogById(id);
            return result.ToHttpResult();
        })
        .WithName("Get Log")
        .WithOpenApi();

        logsGroup.MapGet("/workflow", async (
            [FromQuery] string workflowRunId,
            [FromServices] ILogsService service,
            [FromQuery] int skip = 0,
            [FromQuery] int limit = 50,
            [FromQuery] int? logLevel = null) =>
        {
            var request = new LogsByWorkflowRequest
            {
                WorkflowRunId = workflowRunId,
                Skip = skip,
                Limit = limit,
                LogLevel = logLevel
            };
            var result = await service.GetLogsByWorkflowRunId(request);
            return result.ToHttpResult();
        })
        .WithName("Get Logs by Workflow")
        .WithOpenApi();

        logsGroup.MapGet("/date-range", async (
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromServices] ILogsService service) =>
        {
            var request = new LogsByDateRangeRequest
            {
                StartDate = startDate,
                EndDate = endDate
            };
            var result = await service.GetLogsByDateRange(request);
            return result.ToHttpResult();
        })
        .WithName("Get Logs by Date Range")
        .WithOpenApi();

        logsGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] ILogsService service) =>
        {
            var result = await service.DeleteLog(id);
            if (result.IsSuccess)
            {
                return Results.NoContent();
            }
            return result.ToHttpResult();
        })
        .WithName("Delete Log")
        .WithOpenApi();

    }
}
