using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        // Map certificate endpoints with common attributes
        var certificatesGroup = app.MapGroup("/api/client/certificates")
            .WithTags("WebAPI - Settings")
            .RequiresValidTenant();

        certificatesGroup.MapPost("/generate/base64", (
            HttpContext context,
            [FromBody] CertRequest request,
            [FromServices] CertificateService endpoint) =>
        {
            return endpoint.GenerateClientCertificateBase64(request);
        })
        .WithName("Generate Client Certificate Base64")
        .WithOpenApi(operation => {
            operation.Summary = "Generate a new client certificate in base64 format";
            operation.Description = "Generates and returns a new client certificate in base64 format";
            return operation;
        });

        certificatesGroup.MapPost("/generate", async (
            HttpContext context,
            [FromBody] CertRequest request,
            [FromServices] CertificateService endpoint) =>
        {
            return await endpoint.GenerateClientCertificate(request);
        })
        .WithName("Generate Client Certificate")
        .WithOpenApi(operation => {
            operation.Summary = "Generate a new client certificate";
            operation.Description = "Generates and returns a new client certificate in PFX format";
            return operation;
        });

        certificatesGroup.MapGet("/flowserver/base64", (
            [FromServices] CertificateService endpoint) =>
        {
            var cert = endpoint.GetFlowServerCertBase64();
            var privateKey = endpoint.GetFlowServerPrivateKeyBase64();
            return Results.Ok(new {
                apiKey = cert + ":" + privateKey
            });
        })
        .WithName("Get Flow Server Certificate Base64")
        .WithOpenApi();

        // Map settings endpoints with common attributes
        var settingsGroup = app.MapGroup("/api/client/settings")
            .WithTags("WebAPI - Settings")
            .RequiresValidTenant();

        settingsGroup.MapGet("/flowserver", (
            [FromServices] CertificateService endpoint) =>
        {
            return endpoint.GetFlowServerSettings();
        })
        .WithName("Get Flow Server Settings")
        .WithOpenApi();
    }
} 