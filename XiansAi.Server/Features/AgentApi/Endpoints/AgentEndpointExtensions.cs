using Features.AgentApi.Services.Agent;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;

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
    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("api/agent/signal", async (
            [FromBody] WorkflowSignalRequest request,
            [FromServices] WorkflowSignalService endpoint) =>
        {
            return await endpoint.HandleSignalWorkflow(request);
        })
        .RequiresCertificate();

        return app;
    }
}
