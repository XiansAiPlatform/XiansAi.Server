using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Microsoft.OpenApi.Models;
using Shared.Auth;

namespace Features.AgentApi.Endpoints;

// Non-static class for logger type parameter
public class EventsEndpointLogger {}

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
        var logger = loggerFactory.CreateLogger<EventsEndpointLogger>();
        
        // Map signal endpoints
        var eventGroup = app.MapGroup("/api/agent/event")
            .WithTags("AgentAPI - Event")
            .RequiresCertificate();
            
        eventGroup.MapPost("/by-id", async (
            [FromBody] WorkflowEventRequest request,
            [FromServices] IWorkflowSignalService endpoint) =>
        {
            return await endpoint.SignalWorkflow(request.ToWorkflowSignalRequest());
        })
        .WithOpenApi(operation => {
            operation.Summary = "Send event to workflow";
            operation.Description = "Sends an event to a running workflow instance";
            return operation;
        });

        eventGroup.MapPost("/by-type", async (
            [FromBody] WorkflowEventWithStartRequest request,
            [FromServices] IWorkflowSignalService endpoint) =>
        {
            return await endpoint.SignalWithStartWorkflow(request.ToWorkflowSignalWithStartRequest());
        })
        .WithOpenApi(operation => {
            operation.Summary = "Signal workflow with start";
            operation.Description = "Sends a signal to a running workflow instance and starts a new one if it doesn't exist";
            return operation;
        });
    }
}
