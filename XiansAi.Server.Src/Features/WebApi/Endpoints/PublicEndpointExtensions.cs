using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;
public static class PublicEndpointExtensions
{
    public static void MapPublicEndpoints(this WebApplication app)
    {
        MapRegistrationEndpoints(app);
    }

    private static void MapRegistrationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/public/register/verification/send", async (
            [FromBody] string email,
            [FromServices] PublicService endpoint) =>
        {
            return await endpoint.SendVerificationCode(email);
        })
        .WithName("Send Verification Code")
        .WithOpenApi()
        .RequiresToken();

        app.MapPost("/api/public/register/verification/validate", async (
            [FromBody] ValidateCodeRequest request,
            [FromServices] PublicService endpoint) =>
        {
            var isValid = await endpoint.ValidateCode(request.Email, request.Code);
            return Results.Ok(new { isValid });
        })
        .WithName("Validate Verification Code")
        .WithOpenApi()
        .RequiresToken();

    }
}