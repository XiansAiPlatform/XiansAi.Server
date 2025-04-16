using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;
public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this WebApplication app)
    {
        // Map registration endpoints with common attributes
        var registrationGroup = app.MapGroup("/api/public/register")
            .WithTags("WebAPI - Public Registration")
            .RequiresToken();

        registrationGroup.MapPost("/verification/send", async (
            [FromBody] string email,
            [FromServices] PublicService endpoint) =>
        {
            return await endpoint.SendVerificationCode(email);
        })
        .WithName("Send Verification Code")
        .WithOpenApi();

        registrationGroup.MapPost("/verification/validate", async (
            [FromBody] ValidateCodeRequest request,
            [FromServices] PublicService endpoint) =>
        {
            var isValid = await endpoint.ValidateCode(request.Email, request.Code);
            return Results.Ok(new { isValid });
        })
        .WithName("Validate Verification Code")
        .WithOpenApi();
    }
}