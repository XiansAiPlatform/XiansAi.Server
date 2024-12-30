using Microsoft.AspNetCore.Mvc;

public static class WebClientEndpointExtensions
{
    public static void MapClientEndpoints(this WebApplication app)
    {

        app.MapPost("/api/client/workflows", async (
            HttpContext context,
            [FromServices] WorkflowStarterEndpoint endpoint) =>
        {
            return await endpoint.HandleStartWorkflow(context);
        })
        .WithName("Create New Workflow")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();


        app.MapGet("/api/client/workflows/{workflowId}/events", async (
            HttpContext context,
            [FromServices] WorkflowEventsEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflowEvents(context);
        })
        .WithName("Get Workflow Events")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapGet("/api/client/workflows/{workflowType}/definition", async (
            HttpContext context,
            [FromServices] WorkflowDefinitionEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflowDefinition(context);
        })
        .WithName("Get Workflow Definition")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapGet("/api/client/workflows", async (
            HttpContext context,
            [FromServices] WorkflowFinderEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflows(context);
        })
        .WithName("Get Workflows")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapPost("/api/client/workflows/{workflowId}/cancel", async (
            HttpContext context,
            [FromServices] WorkflowCancelEndpoint endpoint) =>
        {
            return await endpoint.CancelWorkflow(context);
        })
        .WithName("Cancel Workflow")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();
    }
}