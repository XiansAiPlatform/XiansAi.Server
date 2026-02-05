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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Aggregated Metrics Statistics",
            Description = @"Get aggregated metrics statistics for a specific agent within a date range.

Query Parameters (Required):
- agentName: The name of the agent to analyze
- startDate: Start of date range (ISO 8601 format)
- endDate: End of date range (ISO 8601 format)

Query Parameters (Optional):
- activationName: Filter by specific activation within the agent
- participantId: Filter by specific participant/user
- workflowType: Filter by workflow type
- model: Filter by AI model name

Returns:
- Summary statistics (total records, unique categories/types, date range)
- Metrics grouped by category and type with statistics (count, sum, average, min, max, percentiles)
- Breakdown by activation showing metrics for each activation

Examples:
- GET /api/v1/admin/tenants/{tenantId}/metrics/stats?agentName=CustomerSupportAgent&startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z
- GET /api/v1/admin/tenants/{tenantId}/metrics/stats?agentName=CustomerSupportAgent&activationName=email-responder&startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z

Note: All aggregations are performed at the database level for optimal performance."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Time-Series Metrics Data",
            Description = @"Get time-series metrics data for charting and trend analysis for a specific metric.

Query Parameters (Required):
- agentName: The name of the agent to analyze
- category: The metric category (e.g., 'tokens', 'performance', 'cost')
- type: The metric type within the category (e.g., 'total_tokens', 'response_time')
- startDate: Start of date range (ISO 8601 format)
- endDate: End of date range (ISO 8601 format)

Query Parameters (Optional):
- activationName: Filter by specific activation within the agent
- participantId: Filter by specific participant/user
- workflowType: Filter by workflow type
- model: Filter by AI model name
- groupBy: Time granularity - 'day' (default), 'week', or 'month'
- aggregation: Aggregation method - 'sum' (default), 'avg', 'min', 'max', or 'count'
- includeBreakdowns: Include breakdown by activation (default: false)

Returns:
- Data points grouped by time period with values and counts
- Optional breakdowns by activation for each time period
- Summary statistics (total value, count, average, min, max)

Examples:
- GET /api/v1/admin/tenants/{tenantId}/metrics/timeseries?agentName=CustomerSupportAgent&category=tokens&type=total_tokens&startDate=2026-01-01&endDate=2026-01-31
- GET /api/v1/admin/tenants/{tenantId}/metrics/timeseries?agentName=CustomerSupportAgent&category=performance&type=response_time&startDate=2026-01-01&endDate=2026-01-31&groupBy=week&aggregation=avg
- GET /api/v1/admin/tenants/{tenantId}/metrics/timeseries?agentName=CustomerSupportAgent&category=tokens&type=total_tokens&startDate=2026-01-01&endDate=2026-01-31&includeBreakdowns=true

Note: All aggregations are performed at the database level for optimal performance."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Discover Available Metric Categories",
            Description = @"Discover what metric categories and types are available for the tenant.

Query Parameters (Optional):
- startDate: Filter to metrics available since this date
- endDate: Filter to metrics available until this date
- agentName: Filter to metrics for a specific agent
- activationName: Filter to metrics for a specific activation

Returns:
- List of categories with their metric types
- For each metric type: sample count, units, date range, agents using it
- Summary statistics (total categories, types, records, available agents)

Use Cases:
- Dynamically build metric selection UIs
- Discover what metrics are being tracked
- See metric availability over time
- Find which agents report which metrics

Examples:
- GET /api/v1/admin/tenants/{tenantId}/metrics/categories
- GET /api/v1/admin/tenants/{tenantId}/metrics/categories?agentName=CustomerSupportAgent
- GET /api/v1/admin/tenants/{tenantId}/metrics/categories?startDate=2026-01-01&endDate=2026-01-31

Note: This endpoint is useful for building dynamic dashboards that adapt to the metrics being tracked."
        });
    }
}
