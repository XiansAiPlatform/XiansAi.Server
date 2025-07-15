using Features.WebApi.Auth;
using Features.WebApi.Models;
using Features.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;
public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this WebApplication app)
    {
        // Map registration endpoints with common attributes
        var registrationGroup = app.MapGroup("/api/public/register")
            .WithTags("WebAPI - Public Registration");

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
            [FromServices] IPublicService publicService,
            HttpContext httpContext) =>
        {
            var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
            var isValid = await publicService.ValidateCode(request.Email, request.Code, authHeader!.Substring("Bearer ".Length));
            return Results.Ok(new { isValid });
        })
        .WithName("Validate Verification Code")
        .WithOpenApi()
        .RequiresToken();
    }
}