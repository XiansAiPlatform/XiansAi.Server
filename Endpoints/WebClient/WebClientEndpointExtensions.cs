using Microsoft.AspNetCore.Mvc;

public static class WebClientEndpointExtensions
{
    public static void MapClientEndpoints(this WebApplication app)
    {

        app.MapPost("/api/workflows", async (
            HttpContext context,
            [FromServices] WorkflowStarterEndpoint endpoint) =>
        {
            return await endpoint.HandleStartWorkflow(context);
        })
        .WithName("Create New Workflow")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapPost("/api/server/workflows", async (
            HttpContext context,
            [FromServices] WorkflowStarterEndpoint endpoint) =>
            {
                return await endpoint.HandleStartWorkflow(context);
            })
            .WithName("Create New Workflow (Server)")
            .RequireAuthorization("RequireCertificate")
            .WithOpenApi();

        app.MapGet("/api/workflows/{workflowId}/events", async (
            HttpContext context,
            [FromServices] WorkflowEventsEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflowEvents(context);
        })
        .WithName("Get Workflow Events")
        .WithOpenApi();

        app.MapGet("/api/workflows/{workflowType}/definition", async (
            HttpContext context,
            [FromServices] WorkflowDefinitionEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflowDefinition(context);
        })
        .WithName("Get Workflow Definition")
        .WithOpenApi();

        app.MapGet("/api/workflows", async (
            HttpContext context,
            [FromServices] WorkflowFinderEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflows(context);
        })
        .WithName("Get Workflows")
        .WithOpenApi();

        app.MapPost("/api/workflows/{workflowId}/cancel", async (
            HttpContext context,
            [FromServices] WorkflowCancelEndpoint endpoint) =>
        {
            return await endpoint.CancelWorkflow(context);
        })
        .WithName("Cancel Workflow")
        .WithOpenApi();
    }
}