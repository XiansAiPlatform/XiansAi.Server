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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Aggregated Admin Statistics",
            Description = @"Get aggregated statistics for a tenant within a date range.

Query Parameters:
- startDate (required): Start of date range (ISO 8601 format)
- endDate (required): End of date range (ISO 8601 format)
- participantId (optional): Filter statistics by participant user ID

Returns combined statistics from multiple sources:

Tasks Statistics:
- pending: Tasks currently running
- completed: Tasks that completed successfully
- timedOut: Tasks that exceeded their timeout
- cancelled: Tasks that were cancelled or terminated
- total: Total number of tasks in the date range

Messaging Statistics:
- activeUsers: Number of unique users who sent messages
- totalMessages: Total number of messages sent

Examples:
- GET /api/v1/admin/tenants/{tenantId}/stats?startDate=2025-01-01T00:00:00Z&endDate=2025-01-31T23:59:59Z
- GET /api/v1/admin/tenants/{tenantId}/stats?startDate=2025-01-01T00:00:00Z&endDate=2025-01-31T23:59:59Z&participantId=user@example.com

Note: The endpoint filters only Task workflows for task statistics and includes all messages for messaging statistics."
        });
    }
}
