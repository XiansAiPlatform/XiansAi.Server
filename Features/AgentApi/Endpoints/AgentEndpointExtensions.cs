using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.Services.Lib;

namespace XiansAi.Server.Endpoints;

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
            [FromServices] WorkflowSignalEndpoint endpoint) =>
        {
            return await endpoint.HandleSignalWorkflow(request);
        });

        return app;
    }
}