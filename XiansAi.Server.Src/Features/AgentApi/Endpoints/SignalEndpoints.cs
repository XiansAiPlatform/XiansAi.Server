using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Microsoft.OpenApi.Models;
using Shared.Auth;

namespace Features.AgentApi.Endpoints;

// Non-static class for logger type parameter
public class AgentEndpointLogger {}

/// <summary>
/// Provides extension methods for registering agent-communication API endpoints.
/// </summary>
public static class SignalEndpoints
{
    /// <summary>
    /// Maps all agent-related endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <param name="loggerFactory">The logger factory to create a logger for the agent endpoints.</param>
    /// <returns>The web application with agent endpoints configured.</returns>
    public static void MapSignalEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<AgentEndpointLogger>();
        
        // Map info endpoints
        var infoGroup = app.MapGroup("/api/agent/info")
            .WithTags("AgentAPI - Info")
            .RequiresCertificate();
            
        infoGroup.MapGet("", (
            [FromServices] ITenantContext tenantContext
        ) =>
        {
            return "Agent API called by user : " + tenantContext.LoggedInUser + " From Tenant : " + tenantContext.TenantId;
        })
        .WithOpenApi(operation => {
            operation.Summary = "Get agent info";
            operation.Description = "Returns the agent info";
            return operation;
        });
        
        // Map signal endpoints
        var signalGroup = app.MapGroup("/api/agent/signal")
            .WithTags("AgentAPI - Signal")
            .RequiresCertificate();
            
        signalGroup.MapPost("", async (
            [FromBody] WorkflowSignalRequest request,
            [FromServices] IWorkflowSignalService endpoint) =>
        {
            return await endpoint.HandleSignalWorkflow(request);
        })
        .WithOpenApi(operation => {
            operation.Summary = "Signal workflow";
            operation.Description = "Sends a signal to a running workflow instance";
            return operation;
        });
    }
}
