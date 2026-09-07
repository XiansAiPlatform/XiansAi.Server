using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;
using Features.AdminApi.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for reading the audit trail (who did what, and when).
/// These endpoints live under <c>/api/v{version}/admin/tenants/{tenantId}/audit-activities</c>.
/// The tenant is resolved authoritatively by <see cref="AdminRoleTenantResolver"/> and enforced
/// by <see cref="TenantRouteScopeFilter"/>.
/// </summary>
public static class AdminAuditActivityEndpoints
{
    public static void MapAdminAuditActivityEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var auditActivityGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/audit-activities")
            .WithTags("AdminAPI - Audit Activities")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        // Paginated, filterable list of audit activities (newest first).
        auditActivityGroup.MapGet("", async (
            string tenantId,
            [FromServices] IAdminAuditActivityService auditActivityService,
            [FromQuery] string? performedBy = null,
            [FromQuery] string? activationName = null,
            [FromQuery] bool onlyWithoutActivation = false,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var result = await auditActivityService.GetActivitiesAsync(
                tenantId, performedBy, activationName, onlyWithoutActivation, startDate, endDate, page, pageSize);

            return result.ToHttpResult();
        })
        .Produces<AdminAuditActivityListResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("GetAdminAuditActivities")
        .WithSummary("List audit activities")
        .WithDescription(
            "Returns a paginated list of audit trail entries for the tenant, newest first. " +
            "Filter by performedBy, activationName, a createdAt date range, or set onlyWithoutActivation=true " +
            "to list only activities that have no associated activation. Page size is limited to 100.");

        // Distinct "performed by" values, for populating a filter dropdown.
        auditActivityGroup.MapGet("/performed-by", async (
            string tenantId,
            [FromServices] IAdminAuditActivityService auditActivityService) =>
        {
            var result = await auditActivityService.GetPerformedByOptionsAsync(tenantId);
            return result.ToHttpResult();
        })
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("GetAdminAuditActivityPerformedByOptions")
        .WithSummary("List distinct performed-by values")
        .WithDescription("Returns the distinct, non-empty performedBy values recorded for the tenant, sorted alphabetically.");

        // Distinct activation names, for populating a filter dropdown.
        auditActivityGroup.MapGet("/activation-names", async (
            string tenantId,
            [FromServices] IAdminAuditActivityService auditActivityService) =>
        {
            var result = await auditActivityService.GetActivationNameOptionsAsync(tenantId);
            return result.ToHttpResult();
        })
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("GetAdminAuditActivityActivationNameOptions")
        .WithSummary("List distinct activation names")
        .WithDescription("Returns the distinct, non-empty activation names recorded for the tenant, sorted alphabetically.");
    }
}
