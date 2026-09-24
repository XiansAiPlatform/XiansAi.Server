using Microsoft.AspNetCore.Mvc;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Auditing;
using Features.AdminApi.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for reading and writing the audit log (who did what, and when).
/// Tenant-scoped entries live under <c>/api/v{version}/admin/tenants/{tenantId}/audit-logs</c>.
/// Platform-scoped entries (SysAdmin grant/revoke, global user edits, system templates) live
/// under <c>/api/v{version}/admin/platform/audit-logs</c> and are SysAdmin-only.
/// </summary>
public static class AdminAuditLogEndpoints
{
    public static void MapAdminAuditLogEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        MapTenantAuditLogEndpoints(adminApiGroup);
        MapPlatformAuditLogEndpoints(adminApiGroup);
    }

    private static void MapTenantAuditLogEndpoints(RouteGroupBuilder adminApiGroup)
    {
        var auditLogGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/audit-logs")
            .WithTags("AdminAPI - Audit Log")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        auditLogGroup.MapGet("", async (
            string tenantId,
            [FromServices] IAdminAuditLogService auditLogService,
            [FromQuery] string? performedBy = null,
            [FromQuery] string? activationName = null,
            [FromQuery] bool onlyWithoutActivation = false,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            if (AuditLogTenants.IsPlatform(tenantId))
            {
                return Results.NotFound();
            }

            var result = await auditLogService.GetEntriesAsync(
                tenantId, performedBy, activationName, onlyWithoutActivation, startDate, endDate, page, pageSize);

            return result.ToHttpResult();
        })
        .Produces<AdminAuditLogListResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("GetAdminAuditLogs")
        .WithSummary("List audit log entries")
        .WithDescription(
            "Returns a paginated list of audit log entries for the tenant, newest first. " +
            "Filter by performedBy, activationName, a createdAt date range, or set onlyWithoutActivation=true " +
            "to list only entries that have no associated activation. Page size is limited to 100.");

        auditLogGroup.MapGet("/performed-by", async (
            string tenantId,
            [FromServices] IAdminAuditLogService auditLogService) =>
        {
            if (AuditLogTenants.IsPlatform(tenantId))
            {
                return Results.NotFound();
            }

            var result = await auditLogService.GetPerformedByOptionsAsync(tenantId);
            return result.ToHttpResult();
        })
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("GetAdminAuditLogPerformedByOptions")
        .WithSummary("List distinct performed-by values")
        .WithDescription("Returns the distinct, non-empty performedBy values recorded for the tenant, sorted alphabetically.");

        auditLogGroup.MapGet("/activation-names", async (
            string tenantId,
            [FromServices] IAdminAuditLogService auditLogService) =>
        {
            if (AuditLogTenants.IsPlatform(tenantId))
            {
                return Results.NotFound();
            }

            var result = await auditLogService.GetActivationNameOptionsAsync(tenantId);
            return result.ToHttpResult();
        })
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("GetAdminAuditLogActivationNameOptions")
        .WithSummary("List distinct activation names")
        .WithDescription("Returns the distinct, non-empty activation names recorded for the tenant, sorted alphabetically.");

        auditLogGroup.MapPost("", async (
            string tenantId,
            [FromBody] AdminAuditLogCreateRequest request,
            [FromServices] IAdminAuditLogService auditLogService) =>
        {
            if (AuditLogTenants.IsPlatform(tenantId))
            {
                return Results.NotFound();
            }

            var result = await auditLogService.CreateEntryAsync(tenantId, request);
            return result.ToHttpResult();
        })
        .Produces<AuditLogEntry>(StatusCodes.Status201Created)
        .Produces<AuditLogEntry>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .WithName("CreateAdminAuditLog")
        .WithSummary("Record an audit log entry")
        .WithDescription(
            "Persists an admin audit row for the tenant. Performed-by is X-On-Behalf-Of when present " +
            "(otherwise the API-key owner); loggedInUser is always the key owner. " +
            "Repeated conversation.view_as rows for the same admin and target within an hour update the existing row.");
    }

    private static void MapPlatformAuditLogEndpoints(RouteGroupBuilder adminApiGroup)
    {
        var platformGroup = adminApiGroup.MapGroup("/platform/audit-logs")
            .WithTags("AdminAPI - Platform Audit Log")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<SysAdminOnlyFilter>();

        platformGroup.MapGet("", async (
            [FromServices] IAdminAuditLogService auditLogService,
            [FromQuery] string? performedBy = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await auditLogService.GetEntriesAsync(
                AuditLogTenants.Platform, performedBy, activationName: null, onlyWithoutActivation: false,
                startDate, endDate, page, pageSize);

            return result.ToHttpResult();
        })
        .Produces<AdminAuditLogListResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .WithName("GetPlatformAuditLogs")
        .WithSummary("List platform-scoped audit log entries")
        .WithDescription(
            "Returns a paginated list of platform-scoped audit log entries (SysAdmin grant/revoke, " +
            "global user edits, system template changes), newest first. SysAdmin only.");

        platformGroup.MapGet("/performed-by", async (
            [FromServices] IAdminAuditLogService auditLogService) =>
        {
            var result = await auditLogService.GetPerformedByOptionsAsync(AuditLogTenants.Platform);
            return result.ToHttpResult();
        })
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .WithName("GetPlatformAuditLogPerformedByOptions")
        .WithSummary("List distinct performed-by values for platform audit events")
        .WithDescription("Returns the distinct, non-empty performedBy values recorded for platform-scoped audit events.");
    }
}
