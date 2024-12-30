using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authorization;
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
        .WithOpenApi();

        app.MapGet("api/server/debug/certificate", (HttpContext context) =>
        {
            var claims = context.User.Claims.Select(c => new { c.Type, c.Value });
            return Results.Ok(new
            {
                Claims = claims
            });
        });
;
    }
}