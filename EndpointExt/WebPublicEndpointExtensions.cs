using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.EndpointExt.WebClient;

namespace XiansAi.Server.EndpointExt;
public static class WebPublicEndpointExtensions
{
    public static void MapPublicEndpoints(this WebApplication app)
    {
        MapRegistrationEndpoints(app);
    }

    private static void MapRegistrationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/client/register/verification/send", async (
            [FromBody] string email,
            [FromServices] RegistrationEndpoint endpoint) =>
        {
            return await endpoint.SendVerificationCode(email);
        })
        .WithName("Send Verification Code")
        .WithOpenApi()
        .RequireAuthorization("RequireAuth0Auth");

        app.MapPost("/api/client/register/verification/validate", async (
            [FromBody] ValidateCodeRequest request,
            [FromServices] RegistrationEndpoint endpoint) =>
        {
            var isValid = await endpoint.ValidateCode(request.Email, request.Code);
            return Results.Ok(new { isValid });
        })
        .WithName("Validate Verification Code")
        .WithOpenApi()
        .RequireAuthorization("RequireAuth0Auth");

    }
}