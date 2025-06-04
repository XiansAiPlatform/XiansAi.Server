using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Services;

namespace Features.AgentApi.Endpoints.V1;

/// <summary>
/// Provides extension methods for registering settings-related API endpoints for agents.
/// </summary>
public static class SettingsEndpointsV1
{
    /// <summary>
    /// Maps all settings-related endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <returns>The web application with settings endpoints configured.</returns>
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var version = "v1";
        // Map settings endpoints with common attributes
        var settingsGroup = app.MapGroup($"/api/{version}/agent/settings")
            .WithTags($"AgentAPI - Settings {version}")
            .RequiresCertificate();

        // If there are any routes that are common for multiple versions, add them here
        CommonMapRoutes(settingsGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(settingsGroup, version);
    }

    internal static void CommonMapRoutes(RouteGroupBuilder group, string version)
    {
        // If there are any routes that are common for multiple versions, add them here
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        group.MapGet("/flowserver", (
            [FromServices] CertificateService certificateService) =>
        {
            // Get flow server settings
            var settings = certificateService.GetFlowServerSettings();
            return Results.Ok(settings);
        })
        .WithName($"{version} - Get Flow Server Information")
        .WithOpenApi(operation => {
            operation.Summary = "Get Flow Server Information";
            operation.Description = "Returns Flow Server settings and certificate information in a single response";
            return operation;
        });
    }
} 