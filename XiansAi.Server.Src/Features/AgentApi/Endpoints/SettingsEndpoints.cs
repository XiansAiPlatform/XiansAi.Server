using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Services;

namespace Features.AgentApi.Endpoints;

/// <summary>
/// Provides extension methods for registering settings-related API endpoints for agents.
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>
    /// Maps all settings-related endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <returns>The web application with settings endpoints configured.</returns>
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        // Map settings endpoints with common attributes
        var settingsGroup = app.MapGroup("/api/agent/settings")
            .WithTags("AgentAPI - Settings")
            .RequiresCertificate();

        settingsGroup.MapGet("/flowserver", (
            [FromServices] CertificateService certificateService) =>
        {
            // Get flow server settings
            var settings = certificateService.GetFlowServerSettings();
            return Results.Ok(settings);
        })
        .WithName("Get Flow Server Information")
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Get Flow Server Information";
            operation.Description = "Returns Flow Server settings and certificate information in a single response";
            return operation;
        });
    }
} 