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

        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MapRoutes(settingsGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        string RouteKey(string method, string path) => $"{method}:{path}";
        
        if (registeredPaths.Add(RouteKey("GET", "/flowserver")))
        {
            group.MapGet("/flowserver", (
            [FromServices] CertificateService certificateService) =>
            {
                // Get flow server settings
                var settings = certificateService.GetFlowServerSettings();
                return Results.Ok(settings);
            })
            .WithName($"{version} - Get Flow Server Information")
            .WithOpenApi(operation =>
            {
                operation.Summary = "Get Flow Server Information";
                operation.Description = "Returns Flow Server settings and certificate information in a single response";
                return operation;
            });
        }
        
    }
} 