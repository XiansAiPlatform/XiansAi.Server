using System.Text.Json.Nodes;
using Features.AdminApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Runtime management of the OIDC configuration AdminApi's own ID-token-only callers
/// are validated against. Grouped under <c>admin-console</c> alongside the capability matrix: both are
/// global, non-tenant resources changed without a redeploy.
///
/// Deliberately gated by <see cref="SysAdminOnlyFilter"/> rather than the capability matrix, for the
/// same reason as <see cref="AdminCapabilityMatrixEndpoints"/>: this configuration controls who can
/// even authenticate via ID token, so letting a delegated role edit it would let that role widen its
/// own reach into AdminApi the self-widening risk <see cref="CapabilityAction.NonDelegable"/> exists
/// to close for individual actions elsewhere in the catalog.
/// </summary>
public static class AdminConsoleOidcEndpoints
{
    public static void MapAdminConsoleOidcEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var oidcGroup = adminApiGroup.MapGroup("/admin-console/oidc-config")
            .WithTags("AdminAPI - Admin Console OIDC Config")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<SysAdminOnlyFilter>()
            .WithMetadata(TenantOptionalForSysAdminMetadata.Instance);

        oidcGroup.MapGet("", (
            [FromServices] global::Shared.Services.TenantOidcConfigService service) =>
            AdminTenantEndpoints.GetOidcConfig(AdminKeylessUserResolver.AdminConsolePseudoTenant, service))
        .WithName("AdminGetAdminConsoleOidcConfig")
        .WithSummary("Get the admin-console OIDC configuration")
        .WithDescription("Returns the OIDC token-acceptance configuration for AdminApi's own ID-token-only callers, or null when none exists.");

        oidcGroup.MapPost("", UpsertAdminConsoleOidcConfig)
            .WithName("AdminCreateAdminConsoleOidcConfig")
            .WithSummary("Create the admin-console OIDC configuration")
            .WithDescription("Creates or replaces the admin-console OIDC configuration.");

        oidcGroup.MapPut("", UpsertAdminConsoleOidcConfig)
            .WithName("AdminUpdateAdminConsoleOidcConfig")
            .WithSummary("Update the admin-console OIDC configuration")
            .WithDescription("Creates or replaces the admin-console OIDC configuration.");


        oidcGroup.MapDelete("", (
            [FromServices] global::Shared.Services.TenantOidcConfigService service) =>
            AdminTenantEndpoints.DeleteOidcConfig(AdminKeylessUserResolver.AdminConsolePseudoTenant, service))
        .WithName("AdminDeleteAdminConsoleOidcConfig")
        .WithSummary("Delete the admin-console OIDC configuration")
        .WithDescription("Removes the admin-console OIDC configuration.");

        oidcGroup.MapGet("/template", () =>
            Results.Ok(AdminTenantEndpoints.BuildOidcConfigTemplate(AdminKeylessUserResolver.AdminConsolePseudoTenant)))
        .WithName("AdminGetAdminConsoleOidcConfigTemplate")
        .WithSummary("Get an OIDC configuration template")
        .WithDescription("Returns a sample OIDC configuration (with the admin-console tenantId filled in) to use as a starting point.");
    }

    private static Task<IResult> UpsertAdminConsoleOidcConfig(
        [FromBody] JsonObject? config,
        [FromServices] global::Shared.Services.TenantOidcConfigService service,
        [FromServices] ITenantContext tenantContext) =>
        AdminTenantEndpoints.UpsertOidcConfigCore(
            AdminKeylessUserResolver.AdminConsolePseudoTenant, config, service, tenantContext);
}
