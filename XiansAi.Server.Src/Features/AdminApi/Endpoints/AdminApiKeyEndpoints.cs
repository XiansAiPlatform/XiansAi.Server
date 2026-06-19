using System.Text.Json.Serialization;
using Features.AdminApi.Constants;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for two distinct key types:
///
///   Agent Certificates  — X.509 certificates that agents use to authenticate with the flow server.
///                         Managed at: /tenants/{tenantId}/agent-certificates
///                         Scoped by:  <c>certificates.IssuedTo</c>
///
///   Admin API Keys      — Named, revocable/rotatable tokens for programmatic API access.
///                         Managed at: /tenants/{tenantId}/admin-apikeys
///                         Scoped by:  <c>api_keys.created_by</c>
///
/// Every endpoint accepts a <c>userId</c> query parameter that identifies the target user.
/// SysAdmins may supply any userId; other callers may only supply their own.
/// </summary>
public static class AdminApiKeyEndpoints
{
    public sealed class CreateAdminApiKeyRequest
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }
    }

    public static void MapAdminApiKeyEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        MapAgentCertificateEndpoints(adminApiGroup);
        MapAdminApiKeyGroupEndpoints(adminApiGroup);
    }

    // =========================================================================
    // AGENT CERTIFICATES
    // Route: /tenants/{tenantId}/agent-certificates
    // =========================================================================
    private static void MapAgentCertificateEndpoints(RouteGroupBuilder adminApiGroup)
    {
        var group = adminApiGroup.MapGroup("/tenants/{tenantId}/agent-certificates")
            .WithTags("AdminAPI - Agent Certificates")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // POST /tenants/{tenantId}/agent-certificates/generate?userId=&name=&revokePrevious=
        group.MapPost("/generate", async (
            string tenantId,
            [FromQuery] string userId,
            [FromQuery] string? name,
            [FromQuery] bool revokePrevious,
            [FromServices] ITenantContext tenantContext,
            [FromServices] CertificateService certificateService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;
            if (!CanActOnBehalfOf(tenantContext, userId, loggerFactory, out forbid))
                return forbid;

            return await certificateService.GenerateClientCertificateBase64ForUser(userId, revokePrevious, name);
        })
        .WithName("AgentCertificate_Generate")
        .WithSummary("Generate a new agent certificate")
        .WithDescription(
            "Generates a new X.509 client certificate issued to the specified user. " +
            "Supply an optional 'name' to label the certificate for identification in the UI. " +
            "SysAdmins may supply any userId; other callers must supply their own. " +
            "Set revokePrevious=true to revoke all previous certificates issued to the same user.");

        // GET /tenants/{tenantId}/agent-certificates?userId=
        group.MapGet("", async (
            string tenantId,
            [FromQuery] string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] CertificateService certificateService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;
            if (!CanActOnBehalfOf(tenantContext, userId, loggerFactory, out forbid))
                return forbid;

            var certs = await certificateService.ListCertificatesForUserAsync(userId);
            var response = certs.Select(c => new
            {
                c.Id,
                c.Thumbprint,
                c.FriendlyName,
                c.SubjectName,
                c.IssuedTo,
                c.IssuedAt,
                c.ExpiresAt
            });
            return Results.Ok(response);
        })
        .WithName("AgentCertificate_List")
        .WithSummary("List agent certificates for a user")
        .WithDescription(
            "Returns all active agent certificates issued to the specified user. " +
            "Revoked certificates are permanently deleted and will not appear here. " +
            "SysAdmins may supply any userId; other callers must supply their own.");

        // POST /tenants/{tenantId}/agent-certificates/{thumbprint}/revoke?userId=
        group.MapPost("/{thumbprint}/revoke", async (
            string tenantId,
            string thumbprint,
            [FromQuery] string userId,
            [FromQuery] string? reason,
            [FromServices] ITenantContext tenantContext,
            [FromServices] CertificateService certificateService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;
            if (!CanActOnBehalfOf(tenantContext, userId, loggerFactory, out forbid))
                return forbid;

            var effectiveReason = string.IsNullOrWhiteSpace(reason) ? "Revoked by admin" : reason;
            var revoked = await certificateService.RevokeCertificateAsync(thumbprint, effectiveReason, userId);
            return revoked
                ? Results.Ok()
                : Results.NotFound(new { message = "Agent certificate not found or not issued to the specified user." });
        })
        .WithName("AgentCertificate_Revoke")
        .WithSummary("Revoke an agent certificate")
        .WithDescription(
            "Revokes an agent certificate by thumbprint. " +
            "SysAdmins may supply any userId; other callers must supply their own.");
    }

    // =========================================================================
    // ADMIN API KEYS
    // Route: /tenants/{tenantId}/admin-apikeys
    // =========================================================================
    private static void MapAdminApiKeyGroupEndpoints(RouteGroupBuilder adminApiGroup)
    {
        var group = adminApiGroup.MapGroup("/tenants/{tenantId}/admin-apikeys")
            .WithTags("AdminAPI - Admin API Keys")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // POST /tenants/{tenantId}/admin-apikeys?userId=
        group.MapPost("", async (
            string tenantId,
            [FromQuery] string userId,
            [FromBody] CreateAdminApiKeyRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IAdminApiKeyService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;
            if (!CanActOnBehalfOf(tenantContext, userId, loggerFactory, out forbid))
                return forbid;

            var result = await service.CreateApiKeyAsync(tenantId, body.Name, userId);
            if (result.IsSuccess)
            {
                var (apiKey, meta) = result.Data;
                var location = AdminApiConstants.BuildVersionedPath(
                    $"tenants/{Uri.EscapeDataString(tenantId)}/admin-apikeys/{Uri.EscapeDataString(meta.Id)}");
                return Results.Created(location, new
                {
                    apiKey,
                    meta.Id,
                    meta.Name,
                    meta.CreatedAt,
                    meta.CreatedBy
                });
            }
            return result.ToHttpResult();
        })
        .WithName("AdminApiKey_Create")
        .WithSummary("Create an admin API key")
        .WithDescription(
            "Creates a named, revocable admin API key attributed to the specified user. " +
            "SysAdmins may supply any userId; other callers must supply their own.");

        // GET /tenants/{tenantId}/admin-apikeys?userId=
        group.MapGet("", async (
            string tenantId,
            [FromQuery] string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IAdminApiKeyService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;
            if (!CanActOnBehalfOf(tenantContext, userId, loggerFactory, out forbid))
                return forbid;

            var result = await service.ListApiKeysAsync(tenantId, userId);
            if (!result.IsSuccess)
                return result.ToHttpResult();

            var response = (result.Data ?? []).Select(k => new
            {
                k.Id,
                k.Name,
                k.CreatedAt,
                k.CreatedBy,
                k.LastRotatedAt
            });
            return Results.Ok(response);
        })
        .WithName("AdminApiKey_List")
        .WithSummary("List admin API keys for a user")
        .WithDescription(
            "Returns all admin API keys created by the specified user within the tenant. " +
            "SysAdmins may supply any userId; other callers must supply their own.");

        // GET /tenants/{tenantId}/admin-apikeys/{id}?userId=
        group.MapGet("/{id}", async (
            string tenantId,
            string id,
            [FromQuery] string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IAdminApiKeyService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;
            if (!CanActOnBehalfOf(tenantContext, userId, loggerFactory, out forbid))
                return forbid;

            var result = await service.GetApiKeyAsync(id, tenantId, userId);
            if (!result.IsSuccess || result.Data == null)
                return result.ToHttpResult();

            var k = result.Data;
            return Results.Ok(new { k.Id, k.Name, k.CreatedAt, k.CreatedBy, k.LastRotatedAt });
        })
        .WithName("AdminApiKey_Get")
        .WithSummary("Get a single admin API key")
        .WithDescription("Returns an admin API key by ID. Only returns the key if it was created by the specified user.");

        // POST /tenants/{tenantId}/admin-apikeys/{id}/revoke?userId=
        group.MapPost("/{id}/revoke", async (
            string tenantId,
            string id,
            [FromQuery] string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IAdminApiKeyService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;
            if (!CanActOnBehalfOf(tenantContext, userId, loggerFactory, out forbid))
                return forbid;

            var result = await service.RevokeApiKeyAsync(id, tenantId, userId);
            return result.IsSuccess ? Results.Ok() : result.ToHttpResult();
        })
        .WithName("AdminApiKey_Revoke")
        .WithSummary("Revoke an admin API key")
        .WithDescription("Permanently revokes an admin API key. Only keys created by the specified user can be revoked.");

        // POST /tenants/{tenantId}/admin-apikeys/{id}/rotate?userId=
        group.MapPost("/{id}/rotate", async (
            string tenantId,
            string id,
            [FromQuery] string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IAdminApiKeyService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;
            if (!CanActOnBehalfOf(tenantContext, userId, loggerFactory, out forbid))
                return forbid;

            var result = await service.RotateApiKeyAsync(id, tenantId, userId);
            if (result.IsSuccess && result.Data != null)
            {
                var (apiKey, meta) = result.Data.Value;
                return Results.Ok(new
                {
                    apiKey,
                    meta.Id,
                    meta.Name,
                    meta.CreatedAt,
                    meta.CreatedBy,
                    meta.LastRotatedAt
                });
            }
            return result.ToHttpResult();
        })
        .WithName("AdminApiKey_Rotate")
        .WithSummary("Rotate an admin API key")
        .WithDescription("Invalidates the current admin API key and issues a new one. Only keys created by the specified user can be rotated.");
    }

    // =========================================================================
    // Guards
    // =========================================================================

    /// <summary>
    /// Ensures the tenant in the route matches the resolved tenant context.
    /// Returns false with a 403 result via <paramref name="forbid"/> on mismatch.
    /// </summary>
    private static bool TenantRouteMatchesContext(
        ITenantContext ctx,
        string tenantIdFromRoute,
        ILoggerFactory loggerFactory,
        out IResult forbid)
    {
        if (!string.IsNullOrEmpty(tenantIdFromRoute) && !string.IsNullOrEmpty(ctx.TenantId) &&
            string.Equals(tenantIdFromRoute, ctx.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            forbid = Results.Empty;
            return true;
        }

        loggerFactory.CreateLogger("AdminApiKeyEndpoints").LogWarning(
            "Tenant route {RouteTenant} does not match resolved tenant context {CtxTenant}. Caller: {UserId}",
            LogSanitizer.Sanitize(tenantIdFromRoute),
            LogSanitizer.Sanitize(ctx.TenantId),
            LogSanitizer.Sanitize(ctx.LoggedInUser));

        forbid = Results.Json(
            new { message = "Tenant scope mismatch" },
            statusCode: StatusCodes.Status403Forbidden);
        return false;
    }

    /// <summary>
    /// Verifies that the caller is allowed to act on behalf of <paramref name="targetUserId"/>.
    /// SysAdmins may target any user. All other callers may only target themselves.
    /// Returns false with a 403 result via <paramref name="forbid"/> when access is denied.
    /// </summary>
    private static bool CanActOnBehalfOf(
        ITenantContext ctx,
        string targetUserId,
        ILoggerFactory loggerFactory,
        out IResult forbid)
    {
        var isSysAdmin = ctx.UserRoles?.Contains(SystemRoles.SysAdmin) == true;
        var callerUserId = ctx.LoggedInUser ?? string.Empty;

        if (isSysAdmin || string.Equals(callerUserId, targetUserId, StringComparison.OrdinalIgnoreCase))
        {
            forbid = Results.Empty;
            return true;
        }

        loggerFactory.CreateLogger("AdminApiKeyEndpoints").LogWarning(
            "Caller {CallerUserId} attempted to act on behalf of {TargetUserId} without SysAdmin role",
            LogSanitizer.Sanitize(callerUserId),
            LogSanitizer.Sanitize(targetUserId));

        forbid = Results.Json(
            new { message = "Access denied: only SysAdmins can manage keys for other users." },
            statusCode: StatusCodes.Status403Forbidden);
        return false;
    }
}
