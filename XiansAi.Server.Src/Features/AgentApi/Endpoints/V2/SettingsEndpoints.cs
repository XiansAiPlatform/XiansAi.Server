using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Services;

//Boilerplate code for future versions

namespace Features.AgentApi.Endpoints.V2;

public static class SettingsEndpointsV2
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var version = "v2";
        // Map settings endpoints with common attributes
        var settingsGroup = app.MapGroup($"/api/{version}/agent/settings")
            .WithTags($"AgentAPI - Settings {version}")
            .RequiresCertificate();

        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MapRoutes(settingsGroup, version, registeredPaths);

        // Reuse v1 mappings
        V1.SettingsEndpointsV1.MapRoutes(settingsGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        // string RouteKey(string method, string path) => $"{method}:{path}";

        // If v2 has the same endpoint with changes, we can overwrite it, before v1 is called this method will be called and hashset will record that it is already called
        // Hence v1 would not register the same endpoint again

        // var flowServerPath = "/flowserver";
        // if (registeredPaths.Add(RouteKey("GET", flowServerPath)))
        // {
        //     group.MapGet(flowServerPath, (
        //         [FromServices] CertificateService certificateService) =>
        //     {
        //         // Get flow server settings
        //         var settings = certificateService.GetFlowServerSettings();
        //         return Results.Ok(settings);
        //     })
        //     .WithName($"{version} - Get Flow Server Information")
        //     .WithOpenApi(operation =>
        //     {
        //         operation.Summary = "Get Flow Server Information";
        //         operation.Description = "Returns Flow Server settings and certificate information in a single response";
        //         return operation;
        //     });
        // }
    }
} 