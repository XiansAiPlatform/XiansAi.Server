using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

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
            [FromServices] LogsService service) =>
        {
            return await service.GetLogById(id);
        })
        .WithName("Get Log")
        .WithOpenApi();

        logsGroup.MapGet("/workflow", async (
            [FromQuery] string workflowRunId,
            [FromServices] LogsService service,
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
            return await service.GetLogsByWorkflowRunId(request);
        })
        .WithName("Get Logs by Workflow")
        .WithOpenApi();

        logsGroup.MapGet("/date-range", async (
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromServices] LogsService service) =>
        {
            var request = new LogsByDateRangeRequest
            {
                StartDate = startDate,
                EndDate = endDate
            };
            return await service.GetLogsByDateRange(request);
        })
        .WithName("Get Logs by Date Range")
        .WithOpenApi();

        logsGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] LogsService service) =>
        {
            return await service.DeleteLog(id);
        })
        .WithName("Delete Log")
        .WithOpenApi();

        app.MapGet("/api/client/logs/search", async (
            [FromQuery] string searchTerm,
            [FromServices] LogsService service) =>
        {
            return await service.SearchLogs(searchTerm);
        })
        .WithName("Search Logs")
        .WithOpenApi();
    }
}
