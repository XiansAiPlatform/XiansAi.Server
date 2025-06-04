using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils;

namespace Features.AgentApi.Endpoints.V2;

public class AgentEndpointLogger {}

public static class EventsEndpointsV2
{
    public static void MapEventsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v2";
        var logger = loggerFactory.CreateLogger<AgentEndpointLogger>();
        
        // Map signal endpoints
        var signalGroup = app.MapGroup($"/api/{version}/agent/events")
            .WithTags($"AgentAPI - Events {version}")
            .RequiresCertificate();

        // Reuse v1 mappings
        V1.EventsEndpointsV1.CommonMapRoutes(signalGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(signalGroup, version);
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        // You can add new routes specific to v2 here if needed
        // For now, we are reusing the v1 mappings
    }
}
