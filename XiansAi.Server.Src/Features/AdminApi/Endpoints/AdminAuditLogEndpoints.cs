using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;
using Features.AdminApi.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for reading the audit log (who did what, and when).
/// These endpoints live under <c>/api/v{version}/admin/tenants/{tenantId}/audit-logs</c>.
/// The tenant is resolved authoritatively by <see cref="AdminRoleTenantResolver"/> and enforced
/// by <see cref="TenantRouteScopeFilter"/>.
/// </summary>
public static class AdminAuditLogEndpoints
{
    public static void MapAdminAuditLogEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var auditLogGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/audit-logs")
            .WithTags("AdminAPI - Audit Log")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        // Paginated, filterable list of audit log entries (newest first).
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

        // Distinct "performed by" values, for populating a filter dropdown.
        auditLogGroup.MapGet("/performed-by", async (
            string tenantId,
            [FromServices] IAdminAuditLogService auditLogService) =>
        {
            var result = await auditLogService.GetPerformedByOptionsAsync(tenantId);
            return result.ToHttpResult();
        })
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("GetAdminAuditLogPerformedByOptions")
        .WithSummary("List distinct performed-by values")
        .WithDescription("Returns the distinct, non-empty performedBy values recorded for the tenant, sorted alphabetically.");

        // Distinct activation names, for populating a filter dropdown.
        auditLogGroup.MapGet("/activation-names", async (
            string tenantId,
            [FromServices] IAdminAuditLogService auditLogService) =>
        {
            var result = await auditLogService.GetActivationNameOptionsAsync(tenantId);
            return result.ToHttpResult();
        })
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("GetAdminAuditLogActivationNameOptions")
        .WithSummary("List distinct activation names")
        .WithDescription("Returns the distinct, non-empty activation names recorded for the tenant, sorted alphabetically.");
    }
}
