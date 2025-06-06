using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils;

//Boilerplate code for future versions

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

        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MapRoutes(signalGroup, version, registeredPaths);

        // Reuse v1 mappings
        V1.EventsEndpointsV1.MapRoutes(signalGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        // string RouteKey(string method, string path) => $"{method}:{path}";

        // If v2 has the same endpoint with changes, we can overwrite it, before v1 is called this method will be called and hashset will record that it is already called
        // Hence v1 would not register the same endpoint again

        // var startPath = "/with-start";
        // if (registeredPaths.Add(RouteKey("POST", startPath)))
        // {   
        //     group.MapPost(startPath, async (
        //         [FromBody] WorkflowSignalWithStartRequest request,
        //         [FromServices] IWorkflowSignalService endpoint) =>
        //     {
        //         request.SignalName = Constants.SIGNAL_INBOUND_EVENT;
        //         return await endpoint.SignalWithStartWorkflow(request);
        //     })
        //     .WithOpenApi(operation => {
        //         operation.Summary = "Signal workflow with start";
        //         operation.Description = "Sends a signal to a running workflow instance and starts a new one if it doesn't exist";
        //         return operation;
        //     });
        // }
    }
}
