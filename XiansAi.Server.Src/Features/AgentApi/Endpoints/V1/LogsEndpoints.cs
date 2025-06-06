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

        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MapRoutes(logsGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        string RouteKey(string method, string path) => $"{method}:{path}";
        
        if (registeredPaths.Add(RouteKey("POST", "/")))
        {
            group.MapPost("/", async (
            [FromBody] LogRequest[] requests,
            [FromServices] ILogsService service) =>
            {
                return await service.CreateLogs(requests);
            })
            .WithOpenApi(operation =>
            {
                operation.Summary = "Create multiple logs";
                operation.Description = "Creates multiple log entries for workflow monitoring";
                return operation;
            });
        }

        if (registeredPaths.Add(RouteKey("POST", "/single")))
        {
            group.MapPost("/single", async (
                [FromBody] LogRequest request,
                [FromServices] ILogsService service) =>
            {
                return await service.CreateLog(request);
            })
            .WithOpenApi(operation =>
            {
                operation.Summary = "Create single log";
                operation.Description = "Creates a single log entry for workflow monitoring";
                return operation;
            });
        }
    }
} 