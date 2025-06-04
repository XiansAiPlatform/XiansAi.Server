using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

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

        // Reuse v1 mappings
        V1.LogsEndpointsV1.CommonMapRoutes(logsGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(logsGroup, version);
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
        {
        // You can add new routes specific to v2 here if needed
        // For now, we are reusing the v1 mappings
        }
} 