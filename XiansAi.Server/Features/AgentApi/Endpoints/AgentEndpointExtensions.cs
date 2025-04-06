using Features.AgentApi.Services.Agent;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.OpenApi.Models;
using System.Collections.Generic;

namespace Features.AgentApi.Endpoints;

/// <summary>
/// Provides extension methods for registering agent-communication API endpoints.
/// </summary>
public static class AgentEndpointExtensions
{
    /// <summary>
    /// Maps all agent-related endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <returns>The web application with agent endpoints configured.</returns>
    public static void MapAgentEndpoints(this WebApplication app)
    {
        MapSignalEndpoints(app);
    }
    private static void MapSignalEndpoints(this WebApplication app)
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
