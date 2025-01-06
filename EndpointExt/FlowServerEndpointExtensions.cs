using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

public static class FlowServerEndpointExtensions
{
    public static void MapFlowServerEndpoints(this WebApplication app)
    {


        app.MapGet("api/server/debug/certificate", (HttpContext context) =>
        {
            var claims = context.User.Claims.Select(c => new { c.Type, c.Value });
            return Results.Ok(new
            {
                Claims = claims
            });
        });

        app.MapGet("/api/server/instructions/latest", async (
            [FromQuery] string name,
            [FromServices] InstructionsServerEndpoint endpoint) =>
        {
            var result = await endpoint.GetLatestInstruction(name);
            return result;
        });

        app.MapPost("/api/server/activities", async (
            [FromBody] ActivityRequest request,
            [FromServices] ActivitiesServerEndpoint endpoint) =>
        {
            await endpoint.CreateAsync(request);
            return Results.Ok();
        });

    }
}