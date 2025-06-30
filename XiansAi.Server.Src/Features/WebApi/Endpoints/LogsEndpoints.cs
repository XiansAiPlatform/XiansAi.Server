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
    }
}
