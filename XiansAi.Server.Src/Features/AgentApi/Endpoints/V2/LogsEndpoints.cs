using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

//Boilerplate code for future versions

namespace Features.AgentApi.Endpoints.V2;

public static class LogsEndpointsV2
{
    public static void MapLogsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v2";
        var logger = loggerFactory.CreateLogger<AgentEndpointLogger>();
        
        // Map logs endpoints
        var logsGroup = app.MapGroup($"/api/{version}/agent/logs")
            .WithTags($"AgentAPI - Logs {version}")
            .RequiresCertificate();

        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reuse v1 mappings
        MapRoutes(logsGroup, version, registeredPaths);
        V1.LogsEndpointsV1.MapRoutes(logsGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        string RouteKey(string method, string path) => $"{method}:{path}";

        // If v2 has the same endpoint with changes, we can overwrite it, before v1 is called this method will be called and hashset will record that it is already called
        // Hence v1 would not register the same endpoint again

        // var createPath = "/";
        // if (registeredPaths.Add(RouteKey("POST", createPath)))
        // {
        //     group.MapPost(createPath, async (
        //         [FromBody] LogRequest[] requests,
        //         [FromServices] ILogsService service) =>
        //     {
        //         return await service.CreateLogs(requests);
        //     })
        //     .WithOpenApi(operation => {
        //         operation.Summary = "Create multiple logs";
        //         operation.Description = "Creates multiple log entries for workflow monitoring";
        //         return operation;
        //     });
        // }
    }
} 