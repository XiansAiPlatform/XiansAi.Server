using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for aggregated statistics.
/// These endpoints provide combined statistics from various services (tasks, messaging, etc.).
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminStatsEndpoints
{
    /// <summary>
    /// Maps all AdminApi stats endpoints.
    /// </summary>
    public static void MapAdminStatsEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var statsGroup = adminApiGroup.MapGroup("/tenants/{tenantId}")
            .WithTags("AdminAPI - Statistics")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Get aggregated statistics for a tenant
        statsGroup.MapGet("/stats", async (
            string tenantId,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? participantId,
            [FromServices] IAdminStatsService statsService) =>
        {
            // Normalize participantId to lowercase (typically an email)
            if (!string.IsNullOrEmpty(participantId))
            {
                participantId = participantId.ToLowerInvariant();
            }
            
            var result = await statsService.GetStatsAsync(tenantId, startDate, endDate, participantId);
            return result.ToHttpResult();
        })
        .Produces<AdminStatsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminStatistics")
        ;
    }
}
