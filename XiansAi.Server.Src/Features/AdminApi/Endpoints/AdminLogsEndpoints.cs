using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;
using Features.AdminApi.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for workflow execution logs.
/// These endpoints provide query access to logs for monitoring, debugging, and auditing.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminLogsEndpoints
{
    private const int DefaultPageSize = 20;
    private const int DefaultPage = 1;

    // Property filter keys address a single top-level field (properties.<key>), so dots, $ and
    // nested paths are not allowed.
    private static readonly Regex PropertyKeyPattern = new("^[A-Za-z0-9_]{1,64}$", RegexOptions.Compiled);

    /// <summary>
    /// Maps all AdminApi logs endpoints.
    /// </summary>
    public static void MapAdminLogsEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var logsGroup = adminApiGroup.MapGroup("/tenants/{tenantId}")
            .WithTags("AdminAPI - Logs")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

        // Step 1: list distinct log streams (unique workflow_id) sorted by recent activity.
        logsGroup.MapGet("/logs/streams", async (
            string tenantId,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromQuery] string? participantId,
            [FromQuery] string? workflowType,
            [FromQuery] string? logLevel,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? search,
            [FromQuery] int? pageSize,
            [FromQuery] int? page,
            [FromServices] IAdminLogsService logsService) =>
        {
            participantId = NormalizeParticipantId(participantId);
            var logLevels = ParseLogLevels(logLevel);

            var result = await logsService.GetLogStreamsAsync(
                tenantId,
                agentName,
                activationName,
                participantId,
                workflowType,
                logLevels,
                startDate,
                endDate,
                search,
                page ?? DefaultPage,
                pageSize ?? DefaultPageSize);

            return result.ToHttpResult();
        })
        .Produces<AdminLogStreamsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminLogStreams")
        .RequireCapability(CapabilityActions.TenantLogsStreams);

        // Step 2 (or general query): get logs with comprehensive filtering. Supports filtering
        // by one workflow_id (workflowId) or many (workflowIds, comma-separated) - the latter is
        // the typical follow-up after picking streams from /logs/streams.
        // Logs can also be filtered by structured properties with the repeatable
        // property=key:value1,value2 parameter (exact match on properties.<key>, AND across keys).
        logsGroup.MapGet("/logs", async (
            string tenantId,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromQuery] string? participantId,
            [FromQuery] string? workflowId,
            [FromQuery] string? workflowIds,
            [FromQuery] string? workflowType,
            [FromQuery] string? logLevel,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? search,
            [FromQuery] string[]? property,
            [FromQuery] int? pageSize,
            [FromQuery] int? page,
            [FromServices] IAdminLogsService logsService) =>
        {
            participantId = NormalizeParticipantId(participantId);
            var logLevels = ParseLogLevels(logLevel);
            var resolvedWorkflowIds = MergeWorkflowIds(workflowId, workflowIds);
            var (propertyFilters, propertyError) = ParsePropertyFilters(property);
            if (propertyError != null)
            {
                return ServiceResult<AdminLogsResponse>.BadRequest(propertyError).ToHttpResult();
            }

            var result = await logsService.GetLogsAsync(
                tenantId,
                agentName,
                activationName,
                participantId,
                resolvedWorkflowIds,
                workflowType,
                logLevels,
                startDate,
                endDate,
                search,
                page ?? DefaultPage,
                pageSize ?? DefaultPageSize,
                propertyFilters);

            return result.ToHttpResult();
        })
        .Produces<AdminLogsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminLogs")
        .RequireCapability(CapabilityActions.TenantLogsList);

        // Delete all logs for a given agent activation.
        // Note: "activationId" here is the activation's name, not the AgentActivation record's id.
        logsGroup.MapDelete("/logs/agents/{agentName}/activation/{activationId}", async (
            string tenantId,
            string agentName,
            string activationId,
            [FromServices] IAdminLogsService logsService) =>
        {
            var result = await logsService.DeleteLogsByActivationAsync(tenantId, agentName, activationId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = $"Deleted {result.Data} log(s)", deletedCount = result.Data });
        })
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("DeleteLogsByActivation")
        .RequireCapability(CapabilityActions.TenantLogsDeleteByActivation);
    }

    /// <summary>
    /// Participant IDs are typically emails - normalize to lowercase for consistent matching.
    /// </summary>
    private static string? NormalizeParticipantId(string? participantId)
    {
        return string.IsNullOrEmpty(participantId)
            ? participantId
            : participantId.ToLowerInvariant();
    }

    /// <summary>
    /// Parses a comma-separated list of log level names. Unknown names are ignored.
    /// Returns null when nothing parseable was provided.
    /// </summary>
    private static LogLevel[]? ParseLogLevels(string? logLevel)
    {
        if (string.IsNullOrWhiteSpace(logLevel))
        {
            return null;
        }

        var parsedLevels = logLevel
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => Enum.TryParse<LogLevel>(name, ignoreCase: true, out var level)
                ? (LogLevel?)level
                : null)
            .Where(level => level.HasValue)
            .Select(level => level!.Value)
            .ToArray();

        return parsedLevels.Length > 0 ? parsedLevels : null;
    }

    /// <summary>
    /// Merges the legacy single workflowId with the comma-separated workflowIds parameter.
    /// Returns null when neither is supplied.
    /// </summary>
    private static string[]? MergeWorkflowIds(string? singleWorkflowId, string? workflowIdsCsv)
    {
        var ids = new List<string>();

        if (!string.IsNullOrWhiteSpace(singleWorkflowId))
        {
            ids.Add(singleWorkflowId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(workflowIdsCsv))
        {
            ids.AddRange(workflowIdsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (ids.Count == 0)
        {
            return null;
        }

        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Parses repeatable property=key:value1,value2 filters, splitting on the first ':'.
    /// Values of a key given more than once are merged. Returns an error for a missing or
    /// invalid key or for a key without values; returns null filters when none were supplied.
    /// </summary>
    private static (Dictionary<string, string[]>? Filters, string? Error) ParsePropertyFilters(string[]? properties)
    {
        if (properties == null || properties.Length == 0)
        {
            return (null, null);
        }

        var filters = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var separator = property?.IndexOf(':') ?? -1;
            if (separator <= 0)
            {
                return (null, "Property filter must be in the form key:value1,value2");
            }

            var key = property![..separator];
            if (!PropertyKeyPattern.IsMatch(key))
            {
                return (null, "Property filter key may only contain letters, digits and underscores (max 64 characters)");
            }

            var values = property[(separator + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length == 0)
            {
                return (null, $"Property filter '{key}' must have at least one value");
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
