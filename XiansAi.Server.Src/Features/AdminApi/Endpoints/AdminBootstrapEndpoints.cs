using Features.AdminApi.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoint for one-time platform bootstrap.
/// This endpoint is intentionally anonymous: it can only succeed while no users exist in the
/// database, after which it returns a conflict. It creates the first SysAdmin user, ensures a
/// tenant exists, and returns an API key used to configure AgentStudio.
/// </summary>
public static class AdminBootstrapEndpoints
{
    public static void MapAdminBootstrapEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        adminApiGroup.MapGet("/bootstrap", async (
            [FromQuery] string? email,
            [FromQuery] string? tenantId,
            [FromServices] IBootstrapService bootstrapService) =>
        {
            var result = await bootstrapService.BootstrapAsync(email ?? string.Empty, tenantId);
            return result.ToHttpResult();
        })
        .AllowAnonymous()
        .WithTags("AdminAPI - Bootstrap")
        .WithName("BootstrapPlatform");
    }
}
