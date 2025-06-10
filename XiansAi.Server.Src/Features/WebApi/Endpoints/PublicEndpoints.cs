using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;
using Features.WebApi.Models;
using Shared.Utils.Services;

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
            [FromServices] IPublicService publicService) =>
        {
            var result = await publicService.SendVerificationCode(email);
            return result.ToHttpResult();
        })
        .WithName("Send Verification Code")
        .WithOpenApi()
        .RequiresToken();

        registrationGroup.MapPost("/verification/validate", async (
            [FromBody] ValidateCodeRequest request,
            [FromServices] IPublicService publicService) =>
        {
            var isValid = await publicService.ValidateCode(request.Email, request.Code);
            return Results.Ok(new { isValid });
        })
        .WithName("Validate Verification Code")
        .WithOpenApi()
        .RequiresToken();
    }
}