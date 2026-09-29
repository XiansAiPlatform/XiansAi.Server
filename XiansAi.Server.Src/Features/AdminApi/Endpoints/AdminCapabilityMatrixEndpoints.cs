using System.Text.Json.Serialization;
using Features.AdminApi.Auth;
using Features.AdminApi.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Runtime, SysAdmin-only management of the AdminApi capability matrix. Grouped under
/// <c>admin-console</c> alongside the OIDC config: both are global, non-tenant resources changed
/// without a redeploy.
///
/// Deliberately gated by <see cref="SysAdminOnlyFilter"/> rather than the matrix it manages: letting
/// this surface guard itself via <c>RequireCapability</c> would let a SysAdmin delegate "who can edit
/// the matrix" to some other role, which could then widen its own access further — the same
/// self-widening risk <see cref="CapabilityAction.NonDelegable"/> exists to close for individual
/// actions elsewhere in the catalog. Fixing the gate outside the system it governs is what closes it
/// here.
/// </summary>
public static class AdminCapabilityMatrixEndpoints
{
    public sealed class UpsertCapabilityRequest
    {
        /// <summary>Replaces, rather than extends, the action's current rule. Empty means SysAdmin only.</summary>
        [JsonPropertyName("allowedRoles")]
        public required List<string> AllowedRoles { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }

    public static void MapAdminCapabilityMatrixEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var group = adminApiGroup.MapGroup("/admin-console/capability-matrix")
            .WithTags("AdminAPI - Capability Matrix")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<SysAdminOnlyFilter>()
            .WithMetadata(TenantOptionalForSysAdminMetadata.Instance);

        group.MapGet("", async ([FromServices] ICapabilityMatrixService service) =>
        {
            var effective = await service.GetEffectiveAsync();
            return Results.Ok(effective);
        })
        .WithName("AdminGetCapabilityMatrix")
        .WithSummary("Get the effective capability matrix")
        .WithDescription(
            "Every declared action with its resolved allowed roles, whether that came from a stored " +
            "override or the code default. SysAdmin never appears in allowedRoles — it bypasses the " +
            "matrix in code — so effectiveRoles reports it separately.");

        // Mirrors oidc-config/template: what the server compiled in, independent of what is stored.
        group.MapGet("/catalog", () => Results.Ok(new
        {
            actions = CapabilityActions.All.Select(action => new
            {
                action = action.Name,
                description = action.Description,
                defaultRoles = action.DefaultRoles,
                nonDelegable = action.NonDelegable,
            }),
            note = "SysAdmin can perform every action regardless of the matrix; its access is a " +
                   "code-level bypass and is never stored as a role. Actions marked nonDelegable " +
                   "cannot be granted to any other role.",
        }))
        .WithName("AdminGetCapabilityMatrixCatalog")
        .WithSummary("List the declared capability actions")
        .WithDescription("The action catalog as compiled into the server, independent of what is stored.");

        group.MapPut("/{action}", async (
            string action,
            [FromBody] UpsertCapabilityRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ICapabilityMatrixService service) =>
        {
            var actingUserId = tenantContext.LoggedInUser ?? "system";
            var result = await service.UpsertAsync(action, body.AllowedRoles, body.Description, actingUserId);
            return result.ToHttpResult();
        })
        .WithName("AdminUpsertCapabilityMatrixEntry")
        .WithSummary("Set the roles allowed to perform one action")
        .WithDescription(
            "Replaces the action's rule outright rather than adding to it, so a rule can be tightened " +
            "below the code default as well as widened. Unrecognized role strings are saved with a " +
            "warning (the role set is open by design); unknown or non-delegable actions are rejected.");

        group.MapDelete("/{action}", async (
            string action,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ICapabilityMatrixService service) =>
        {
            var actingUserId = tenantContext.LoggedInUser ?? "system";
            var result = await service.DeleteAsync(action, actingUserId);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        })
        .WithName("AdminDeleteCapabilityMatrixEntry")
        .WithSummary("Revert one action to its code default")
        .WithDescription(
            "Removes the stored rule. The action reverts to the default compiled into the server, not " +
            "to deny — deleting a rule can never close an action off.");
    }
}
