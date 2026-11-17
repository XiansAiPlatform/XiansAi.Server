using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Data.Models.Usage;
using Shared.Services;
using Shared.Utils.Services;
using Features.AdminApi.Auth;

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
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

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
            [FromQuery] string[]? metadata,
            [FromQuery] string? breakdownBy,
            [FromServices] IAdminMetricsService metricsService,
            CancellationToken cancellationToken) =>
        {
            // Normalize participantId to lowercase (typically an email)
            if (!string.IsNullOrEmpty(participantId))
            {
                participantId = participantId.ToLowerInvariant();
            }

            var (metadataFilters, metadataError) = ParseMetadataFilters(metadata);
            if (metadataError != null)
            {
                return ServiceResult<AdminMetricsStatsResponse>.BadRequest(metadataError).ToHttpResult();
            }

            breakdownBy = string.IsNullOrWhiteSpace(breakdownBy) ? null : breakdownBy;
            if (breakdownBy != null && !MetricMetadataKey.IsValid(breakdownBy))
            {
                return ServiceResult<AdminMetricsStatsResponse>
                    .BadRequest($"breakdownBy {MetricMetadataKey.Requirement}").ToHttpResult();
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
                Model = model,
                Metadata = metadataFilters,
                BreakdownBy = breakdownBy
            };

            var result = await metricsService.GetMetricsStatsAsync(request, cancellationToken);
            return result.ToHttpResult();
        })
        .Produces<AdminMetricsStatsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminMetricsStats")
        .RequireCapability(CapabilityActions.TenantMetricsStats);

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
            [FromQuery] bool includeBreakdowns = false,
            [FromQuery] string[]? metadata = null) =>
        {
            // Normalize participantId to lowercase (typically an email)
            if (!string.IsNullOrEmpty(participantId))
            {
                participantId = participantId.ToLowerInvariant();
            }

            var (metadataFilters, metadataError) = ParseMetadataFilters(metadata);
            if (metadataError != null)
            {
                return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest(metadataError).ToHttpResult();
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
                Metadata = metadataFilters,
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
        .RequireCapability(CapabilityActions.TenantMetricsTimeSeries);

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
        .RequireCapability(CapabilityActions.TenantMetricsCategories);

        // Delete all performance-metric records for a given agent activation.
        // Note: "activationId" here is the activation's name, not the AgentActivation record's id.
        metricsGroup.MapDelete("/agents/{agentName}/activation/{activationId}", async (
            string tenantId,
            string agentName,
            string activationId,
            [FromServices] IAdminMetricsService metricsService,
            CancellationToken cancellationToken) =>
        {
            var result = await metricsService.DeleteMetricsByActivationAsync(tenantId, agentName, activationId, cancellationToken);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = $"Deleted {result.Data} metric(s)", deletedCount = result.Data });
        })
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("DeleteMetricsByActivation")
        .RequireCapability(CapabilityActions.TenantMetricsDeleteByActivation);
    }

    /// <summary>
    /// Parses repeatable metadata=key:value1,value2 filters, splitting on the first ':'.
    /// Values for a repeated key are merged (OR); different keys are combined with AND.
    /// Deliberately the same shape and rules as the admin logs property filter
    /// (AdminLogsEndpoints.ParsePropertyFilters) so the two parameters read the same.
    /// </summary>
    internal static (Dictionary<string, string[]>? Filters, string? Error) ParseMetadataFilters(string[]? metadata)
    {
        if (metadata == null || metadata.Length == 0)
        {
            return (null, null);
        }

        var filters = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in metadata)
        {
            var separator = entry?.IndexOf(':') ?? -1;
            if (separator <= 0)
            {
                return (null, "Metadata filter must be in the form key:value1,value2");
            }

            var key = entry![..separator];
            if (!MetricMetadataKey.IsValid(key))
            {
                return (null, $"Metadata filter key {MetricMetadataKey.Requirement}");
            }

            var values = entry[(separator + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length == 0)
            {
                return (null, $"Metadata filter '{key}' must have at least one value");
            }

            if (!filters.TryGetValue(key, out var existing))
            {
                filters[key] = existing = new List<string>();
            }
            existing.AddRange(values);
        }

        return (filters.ToDictionary(
            filter => filter.Key,
            filter => filter.Value.Distinct(StringComparer.Ordinal).ToArray()), null);
    }
}
