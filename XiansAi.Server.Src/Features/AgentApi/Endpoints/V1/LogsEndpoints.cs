using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

namespace Features.AgentApi.Endpoints.V1;

public static class LogsEndpointsV1
{
    public static void MapLogsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v1";
        var logger = loggerFactory.CreateLogger<AgentEndpointLogger>();
        
        // Map logs endpoints
        var logsGroup = app.MapGroup($"/api/{version}/agent/logs")
            .WithTags($"AgentAPI - Logs {version}")
            .RequiresCertificate();

        // If there are any routes that are common for multiple versions, add them here
        CommonMapRoutes(logsGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(logsGroup, version);
    }

    internal static void CommonMapRoutes(RouteGroupBuilder group, string version)
    {
        // If there are any routes that are common for multiple versions, add them here
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        group.MapPost("/", async (
            [FromBody] LogRequest[] requests,
            [FromServices] ILogsService service) =>
        {
            return await service.CreateLogs(requests);
        })
        .WithOpenApi(operation => {
            operation.Summary = "Create multiple logs";
            operation.Description = "Creates multiple log entries for workflow monitoring";
            return operation;
        });

        group.MapPost("/single", async (
            [FromBody] LogRequest request,
            [FromServices] ILogsService service) =>
        {
            return await service.CreateLog(request);
        })
        .WithOpenApi(operation => {
            operation.Summary = "Create single log";
            operation.Description = "Creates a single log entry for workflow monitoring";
            return operation;
        });
    }
} 