using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Services;
using Shared.Utils.Services;
using Features.WebApi.Models;

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
            [FromQuery] LogLevel? logLevel,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] int? pageSize,
            [FromQuery] int? page,
            [FromServices] IAdminLogsService logsService) =>
        {
            // Set defaults for pagination
            var actualPage = page ?? 1;
            var actualPageSize = pageSize ?? 20;

            var result = await logsService.GetLogsAsync(
                tenantId,
                agentName,
                activationName,
                participantId,
                workflowId,
                workflowType,
                logLevel,
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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Workflow Execution Logs",
            Description = @"Retrieve workflow execution logs with comprehensive filtering options.

Query Parameters:
- agentName (optional): Filter by agent name (exact match)
- activationName (optional): Filter by activation name
- participantId (optional): Filter by participant user ID
- workflowId (optional): Filter by specific workflow ID
- workflowType (optional): Filter by workflow type
- logLevel (optional): Filter by log level (0=Trace, 1=Debug, 2=Information, 3=Warning, 4=Error, 5=Critical)
- startDate (optional): Start of date range (ISO 8601 format)
- endDate (optional): End of date range (ISO 8601 format)
- page (optional): Page number (default: 1)
- pageSize (optional): Items per page (default: 20, max: 100)

Returns paginated log entries with:
- Log details (message, level, timestamps)
- Workflow information (ID, run ID, type)
- Agent and activation information
- Participant information
- Custom properties and exception details (if any)

Examples:
- Get all logs: GET /api/v1/admin/tenants/default/logs
- Filter by agent: GET /api/v1/admin/tenants/default/logs?agentName=Order%20Manager%20Agent
- Filter by activation: GET /api/v1/admin/tenants/default/logs?activationName=Order%20Manager%20Agent%20-%20Remote%20Peafowl
- Error logs only: GET /api/v1/admin/tenants/default/logs?logLevel=4
- Date range: GET /api/v1/admin/tenants/default/logs?startDate=2025-01-01T00:00:00Z&endDate=2025-01-31T23:59:59Z
- Combined filters: GET /api/v1/admin/tenants/default/logs?agentName=Order%20Manager%20Agent&logLevel=4&pageSize=50"
        });
    }
}
