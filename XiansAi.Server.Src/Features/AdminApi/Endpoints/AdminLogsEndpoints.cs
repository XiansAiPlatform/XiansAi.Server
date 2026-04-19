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
    /// <summary>
    /// Maps all AdminApi logs endpoints.
    /// </summary>
    public static void MapAdminLogsEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var logsGroup = adminApiGroup.MapGroup("/tenants/{tenantId}")
            .WithTags("AdminAPI - Logs")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Get logs with comprehensive filtering
        logsGroup.MapGet("/logs", async (
            string tenantId,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromQuery] string? participantId,
            [FromQuery] string? workflowId,
            [FromQuery] string? workflowType,
            [FromQuery] string? logLevel,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] int? pageSize,
            [FromQuery] int? page,
            [FromServices] IAdminLogsService logsService) =>
        {
            // Normalize participantId to lowercase (typically an email)
            if (!string.IsNullOrEmpty(participantId))
            {
                participantId = participantId.ToLowerInvariant();
            }
            
            // Set defaults for pagination
            var actualPage = page ?? 1;
            var actualPageSize = pageSize ?? 20;

            // Parse multiple log levels from comma-separated string
            LogLevel[]? logLevels = null;
            if (!string.IsNullOrWhiteSpace(logLevel))
            {
                var logLevelNames = logLevel.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToArray();
                    
                var parsedLevels = new List<LogLevel>();
                foreach (var levelName in logLevelNames)
                {
                    if (Enum.TryParse<LogLevel>(levelName, true, out var level))
                    {
                        parsedLevels.Add(level);
                    }
                }
                logLevels = parsedLevels.Count > 0 ? parsedLevels.ToArray() : null;
            }

            var result = await logsService.GetLogsAsync(
                tenantId,
                agentName,
                activationName,
                participantId,
                workflowId,
                workflowType,
                logLevels,
                startDate,
                endDate,
                actualPage,
                actualPageSize);

            return result.ToHttpResult();
        })
        .Produces<AdminLogsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("GetAdminLogs")
        ;
    }
}
