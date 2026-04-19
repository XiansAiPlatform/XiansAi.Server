using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Shared.Services;

namespace Features.WebApi.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        // Map certificate endpoints with common attributes
        var settingsGroup = app.MapGroup("/api/client/settings")
            .WithTags("WebAPI - Settings")
            .RequiresValidTenant()
            .RequireAuthorization();

        settingsGroup.MapPost("/appserver/base64cert", (
            HttpContext context,
            [FromServices] CertificateService endpoint,
            [FromQuery] bool revoke_previous = false) =>
        {
            return endpoint.GenerateClientCertificateBase64(revoke_previous);
        })
        .WithName("Generate Client Certificate Base64")
        
        .WithSummary("Generate a new client certificate in base64 format")
        .WithDescription("Generates and returns a new client certificate in base64 format");

    }
} 