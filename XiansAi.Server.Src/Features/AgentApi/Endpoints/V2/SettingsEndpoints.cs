using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Services;

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

        // Reuse v1 mappings
        V1.SettingsEndpointsV1.CommonMapRoutes(settingsGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(settingsGroup, version);
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        // You can add new routes specific to v2 here if needed
        // For now, we are reusing the v1 mappings
    }
} 