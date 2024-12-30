using Microsoft.AspNetCore.Mvc;

public static class FlowServerEndpointExtensions
{
    public static void MapFlowServerEndpoints(this WebApplication app)
    {
        app.MapPost("/api/server/definitions", async (
            HttpContext context,
            [FromServices] WorkflowDefinitionEndpoint endpoint) =>
        {
            return await endpoint.RegisterWorkflowDefinition(context);
        })
        .WithName("Register Workflow Definition")
        .RequireAuthorization("RequireCertificate")
        .WithOpenApi();
    }
}