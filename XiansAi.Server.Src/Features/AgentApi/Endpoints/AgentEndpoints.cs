using Features.AgentApi.Services.Agent;
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
public static class AgentEndpoints
{
    /// <summary>
    /// Maps all agent-related endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <param name="loggerFactory">The logger factory to create a logger for the agent endpoints.</param>
    /// <returns>The web application with agent endpoints configured.</returns>
    public static void MapAgentEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<AgentEndpointLogger>();
        MapInfoEndpoints(app, logger);
        MapSignalEndpoints(app, logger);
    }
    private static void MapInfoEndpoints(this WebApplication app, ILogger<AgentEndpointLogger> logger)
    {
        app.MapGet("api/agent/info", (
            [FromServices] ITenantContext tenantContext
        ) =>
        {
            return "Agent API called by user : " + tenantContext.LoggedInUser + " From Tenant : " + tenantContext.TenantId;
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Get agent info";
            operation.Description = "Returns the agent info";
            operation.Tags = new List<OpenApiTag> { new OpenApiTag { Name = "AgentAPI - Info" } };
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            return operation;
        });
    }
    private static void MapSignalEndpoints(this WebApplication app, ILogger<AgentEndpointLogger> logger)
    {
        app.MapPost("api/agent/signal", async (
            [FromBody] WorkflowSignalRequest request,
            [FromServices] WorkflowSignalService endpoint) =>
        {
            return await endpoint.HandleSignalWorkflow(request);
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Signal workflow";
            operation.Description = "Sends a signal to a running workflow instance";
            operation.Tags = new List<OpenApiTag> { new OpenApiTag { Name = "AgentAPI - Signal" } };
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            return operation;
        });
    }
}
