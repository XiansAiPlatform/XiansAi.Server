using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;

namespace Features.WebApi.Endpoints;

public static class SettingsEndpointExtensions
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/client/certificates/generate/base64", (
            HttpContext context,
            [FromBody] CertRequest request,
            [FromServices] CertificateService endpoint) =>
        {
            return endpoint.GenerateClientCertificateBase64(request);
        })
        .WithName("Generate Client Certificate Base64")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Generate a new client certificate in base64 format";
            operation.Description = "Generates and returns a new client certificate in base64 format";
            return operation;
        });

        app.MapPost("/api/client/certificates/generate", async (
            HttpContext context,
            [FromBody] CertRequest request,
            [FromServices] CertificateService endpoint) =>
        {
            return await endpoint.GenerateClientCertificate(request);
        })
        .WithName("Generate Client Certificate")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Generate a new client certificate";
            operation.Description = "Generates and returns a new client certificate in PFX format";
            return operation;
        });

        app.MapGet("/api/client/settings/flowserver", (
            [FromServices] CertificateService endpoint) =>
        {
            return endpoint.GetFlowServerSettings();
        })
        .WithName("Get Flow Server Settings")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(); 

        app.MapGet("/api/client/certificates/flowserver/base64", (
            [FromServices] CertificateService endpoint) =>
        {
            var cert = endpoint.GetFlowServerCertBase64();
            var privateKey = endpoint.GetFlowServerPrivateKeyBase64();
            return Results.Ok(new {
                apiKey = cert + ":" + privateKey
            });
        })
        .WithName("Get Flow Server Certificate Base64")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(); 
    }
} 