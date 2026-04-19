using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Data.Models.Usage;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for metrics and analytics.
/// Provides performance metrics, time-series data, and metric discovery for agents.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminMetricsEndpoints
{
    /// <summary>
    /// Maps all AdminApi metrics endpoints.
    /// </summary>
    public static void MapAdminMetricsEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var metricsGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/metrics")
            .WithTags("AdminAPI - Metrics")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Get aggregated metrics statistics
        metricsGroup.MapGet("/stats", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] string? activationName,
            [FromQuery] string? participantId,
            [FromQuery] string? workflowType,
            [FromQuery] string? model,
            [FromServices] IAdminMetricsService metricsService,
            CancellationToken cancellationToken) =>
        {
            // Normalize participantId to lowercase (typically an email)
            if (!string.IsNullOrEmpty(participantId))
            {
                participantId = participantId.ToLowerInvariant();
            }
            
            var request = new AdminMetricsStatsRequest
            {
                TenantId = tenantId,
                AgentName = agentName,
                StartDate = startDate,
                EndDate = endDate,
                ActivationName = activationName,
                ParticipantId = participantId,
                WorkflowType = workflowType,
                Model = model
            };

            var result = await metricsService.GetMetricsStatsAsync(request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminMetricsStatsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminMetricsStats")
        ;

        // Get time-series metrics data
        metricsGroup.MapGet("/timeseries", async (
            string tenantId,
            [FromServices] IAdminMetricsService metricsService,
            CancellationToken cancellationToken,
            [FromQuery] string? agentName = null,
            [FromQuery] string? category = null,
            [FromQuery] string? type = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? activationName = null,
            [FromQuery] string? participantId = null,
            [FromQuery] string? workflowType = null,
            [FromQuery] string? model = null,
            [FromQuery] string groupBy = "day",
            [FromQuery] string aggregation = "sum",
            [FromQuery] bool includeBreakdowns = false) =>
        {
            // Normalize participantId to lowercase (typically an email)
            if (!string.IsNullOrEmpty(participantId))
            {
                participantId = participantId.ToLowerInvariant();
            }
            
            var request = new AdminMetricsTimeSeriesRequest
            {
                TenantId = tenantId,
                AgentName = agentName ?? string.Empty,
                Category = category ?? string.Empty,
                Type = type ?? string.Empty,
                StartDate = startDate ?? DateTime.MinValue,
                EndDate = endDate ?? DateTime.MinValue,
                ActivationName = activationName,
                ParticipantId = participantId,
                WorkflowType = workflowType,
                Model = model,
                GroupBy = groupBy,
                Aggregation = aggregation,
                IncludeBreakdowns = includeBreakdowns
            };

            var result = await metricsService.GetMetricsTimeSeriesAsync(request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminMetricsTimeSeriesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminMetricsTimeSeries")
        ;

        // Discover available metric categories and types
        metricsGroup.MapGet("/categories", async (
            string tenantId,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromServices] IAdminMetricsService metricsService,
            CancellationToken cancellationToken) =>
        {
            var request = new AdminMetricsCategoriesRequest
            {
                TenantId = tenantId,
                StartDate = startDate,
                EndDate = endDate,
                AgentName = agentName,
                ActivationName = activationName
            };

            var result = await metricsService.GetMetricsCategoriesAsync(request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminMetricsCategoriesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminMetricsCategories")
        ;
    }
}
