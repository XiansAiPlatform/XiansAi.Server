using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;

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

    /// <summary>
    /// Maps all AdminApi logs endpoints.
    /// </summary>
    public static void MapAdminLogsEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var logsGroup = adminApiGroup.MapGroup("/tenants/{tenantId}")
            .WithTags("AdminAPI - Logs")
            .RequireAuthorization("AdminEndpointAuthPolicy");

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
                page ?? DefaultPage,
                pageSize ?? DefaultPageSize);

            return result.ToHttpResult();
        })
        .Produces<AdminLogStreamsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminLogStreams");

        // Step 2 (or general query): get logs with comprehensive filtering. Supports filtering
        // by one workflow_id (workflowId) or many (workflowIds, comma-separated) - the latter is
        // the typical follow-up after picking streams from /logs/streams.
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
            [FromQuery] int? pageSize,
            [FromQuery] int? page,
            [FromServices] IAdminLogsService logsService) =>
        {
            participantId = NormalizeParticipantId(participantId);
            var logLevels = ParseLogLevels(logLevel);
            var resolvedWorkflowIds = MergeWorkflowIds(workflowId, workflowIds);

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
                page ?? DefaultPage,
                pageSize ?? DefaultPageSize);

            return result.ToHttpResult();
        })
        .Produces<AdminLogsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminLogs");
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
}
