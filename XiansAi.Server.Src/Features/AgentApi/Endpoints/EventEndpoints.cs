using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils;

namespace Features.AgentApi.Endpoints;

// Non-static class for logger type parameter
public class AgentEndpointLogger {}

/// <summary>
/// Provides extension methods for registering agent-communication API endpoints.
/// </summary>
public static class EventsEndpoints
{
    /// <summary>
    /// Maps all agent-related endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <param name="loggerFactory">The logger factory to create a logger for the agent endpoints.</param>
    /// <returns>The web application with agent endpoints configured.</returns>
    public static void MapEventsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<AgentEndpointLogger>();
        
        // Map signal endpoints
        var signalGroup = app.MapGroup("/api/agent/events")
            .WithTags("AgentAPI - Events")
            .RequiresCertificate();

        signalGroup.MapPost("/with-start", async (
            [FromBody] WorkflowSignalWithStartRequest request,
            [FromServices] IWorkflowSignalService endpoint) =>
        {
            request.SignalName = Constants.SIGNAL_INBOUND_EVENT;
            return await endpoint.SignalWithStartWorkflow(request);
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Signal workflow with start";
            operation.Description = "Sends a signal to a running workflow instance and starts a new one if it doesn't exist";
            return operation;
        });
    }
}
