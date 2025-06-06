using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils;

namespace Features.AgentApi.Endpoints.V1;

// Non-static class for logger type parameter
public class AgentEndpointLogger {}

/// <summary>
/// Provides extension methods for registering agent-communication API endpoints.
/// </summary>
public static class EventsEndpointsV1
{
    /// <summary>
    /// Maps all agent-related endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <param name="loggerFactory">The logger factory to create a logger for the agent endpoints.</param>
    /// <returns>The web application with agent endpoints configured.</returns>
    public static void MapEventsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v1";
        var logger = loggerFactory.CreateLogger<AgentEndpointLogger>();
        
        // Map signal endpoints
        var signalGroup = app.MapGroup($"/api/{version}/agent/events")
            .WithTags($"AgentAPI - Events {version}")
            .RequiresCertificate();

        // If there are any routes that will be deleted in future versions, add them here
        MapRoutes(signalGroup, version, new HashSet<string>());
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        string RouteKey(string method, string path) => $"{method}:{path}";

        if (registeredPaths.Add(RouteKey("POST", "/with-start")))
        {
            group.MapPost("/with-start", async (
                [FromBody] WorkflowSignalWithStartRequest request,
                [FromServices] IWorkflowSignalService endpoint) =>
            {
                request.SignalName = Constants.SIGNAL_INBOUND_EVENT;
                return await endpoint.SignalWithStartWorkflow(request);
            })
            .WithOpenApi(operation =>
            {
                operation.Summary = "Signal workflow with start";
                operation.Description = "Sends a signal to a running workflow instance and starts a new one if it doesn't exist";
                return operation;
            });
        }
    }
}
