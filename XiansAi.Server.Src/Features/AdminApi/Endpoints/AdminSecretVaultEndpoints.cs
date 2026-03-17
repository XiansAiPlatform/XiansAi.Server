using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

public static class AdminSecretVaultEndpoints
{
    public static void MapAdminSecretVaultEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var group = adminApiGroup.MapGroup("/secrets")
            .WithTags("AdminAPI - Secret Vault")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        group.MapPost("", async (
            [FromBody] SecretVaultCreateRequest request,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantCacheService tenantCacheService,
            CancellationToken cancellationToken) =>
        {
            if (!SecretVaultScopeEnforcement.TryResolveScope(
                tenantContext,
                request.TenantId,
                request.AgentId,
                request.UserId,
                request.ActivationName,
                out var effectiveTenantId,
                out var effectiveAgentId,
                out var effectiveUserId,
                out var effectiveActivationName,
                out var forbiddenResult))
            {
                return forbiddenResult!.ToHttpResult();
            }

            // If a tenant scope is provided/resolved, ensure the tenant exists when tenantId scope is explicitly set.
            if (!string.IsNullOrWhiteSpace(request.TenantId) && !string.IsNullOrWhiteSpace(effectiveTenantId))
            {
                var tenant = await tenantCacheService.GetByTenantIdAsync(effectiveTenantId, cancellationToken);
                if (tenant == null)
                {
                    return ServiceResult<SecretVaultGetResponse>.NotFound("Tenant not found").ToHttpResult();
                }
            }

            var actor = tenantContext.LoggedInUser ?? "system";
            var input = new SecretVaultCreateInput(
                request.Key,
                request.Value,
                effectiveTenantId,
                effectiveAgentId,
                effectiveUserId,
                effectiveActivationName,
                SecretVaultService.NormalizeAdditionalDataFromRequest(request.AdditionalData));
            var result = await service.CreateAsync(input, actor);
            return result.ToHttpResult();
        })
        .WithName("CreateSecret")
        .WithOpenApi(o => { o.Summary = "Create secret"; o.Description = "Create a new secret. Key must be unique."; return o; });

        group.MapGet("", async (
            [FromQuery] string? tenantId,
            [FromQuery] string? agentId,
            [FromQuery] string? activationName,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (!SecretVaultScopeEnforcement.TryResolveScope(
                tenantContext,
                tenantId,
                agentId,
                null,
                activationName,
                out var effectiveTenantId,
                out var effectiveAgentId,
                out _,
                out var effectiveActivationName,
                out var forbiddenResult))
            {
                return forbiddenResult!.ToHttpResult();
            }

            var result = await service.ListAsync(effectiveTenantId, effectiveAgentId, effectiveActivationName);
            return result.ToHttpResult();
        })
        .WithName("ListSecrets")
        .WithOpenApi(o => { o.Summary = "List secrets"; o.Description = "List secrets with optional tenantId/agentId/activationName filter."; return o; });

        group.MapGet("/fetch", async (
            [FromQuery] string key,
            [FromQuery] string? tenantId,
            [FromQuery] string? agentId,
            [FromQuery] string? userId,
            [FromQuery] string? activationName,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (!SecretVaultScopeEnforcement.TryResolveScope(
                tenantContext,
                tenantId,
                agentId,
                userId,
                activationName,
                out var effectiveTenantId,
                out var effectiveAgentId,
                out var effectiveUserId,
                out var effectiveActivationName,
                out var forbiddenResult))
            {
                return forbiddenResult!.ToHttpResult();
            }

            var result = await service.FetchByKeyAsync(key, effectiveTenantId, effectiveAgentId, effectiveUserId, effectiveActivationName);
            return result.ToHttpResult();
        })
        .WithName("FetchSecretByKey")
        .WithOpenApi(o => { o.Summary = "Fetch secret by key"; o.Description = "Returns decrypted value and optional AdditionalData. Scope by tenantId, agentId, userId, activationName."; return o; });

        group.MapPut("", async (
            [FromBody] SecretVaultUpdateRequest request,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantCacheService tenantCacheService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Key))
                return ServiceResult<SecretVaultGetResponse>.BadRequest("Key is required").ToHttpResult();

            if (!SecretVaultScopeEnforcement.TryResolveScope(
                tenantContext,
                request.TenantId,
                request.AgentId,
                request.UserId,
                request.ActivationName,
                out var effectiveTenantId,
                out var effectiveAgentId,
                out var effectiveUserId,
                out var effectiveActivationName,
                out var forbiddenResult))
            {
                return forbiddenResult!.ToHttpResult();
            }

            // If a tenant scope is provided/resolved, ensure the tenant exists when tenantId scope is explicitly set.
            if (!string.IsNullOrWhiteSpace(request.TenantId) && !string.IsNullOrWhiteSpace(effectiveTenantId))
            {
                var tenant = await tenantCacheService.GetByTenantIdAsync(effectiveTenantId, cancellationToken);
                if (tenant == null)
                {
                    return ServiceResult<SecretVaultGetResponse>.NotFound("Tenant not found").ToHttpResult();
                }
            }

            var actor = tenantContext.LoggedInUser ?? "system";
            var input = new SecretVaultUpdateInput(
                request.Value,
                effectiveTenantId,
                effectiveAgentId,
                effectiveUserId,
                effectiveActivationName,
                SecretVaultService.NormalizeAdditionalDataFromRequest(request.AdditionalData));
            var result = await service.UpdateByKeyAsync(request.Key, input, actor);
            return result.ToHttpResult();
        })
        .WithName("UpdateSecretByKey")
        .WithOpenApi(o => { o.Summary = "Update secret by key and scope"; return o; });

        group.MapDelete("", async (
            [FromQuery] string key,
            [FromQuery] string? tenantId,
            [FromQuery] string? agentId,
            [FromQuery] string? userId,
            [FromQuery] string? activationName,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (string.IsNullOrWhiteSpace(key))
                return ServiceResult<bool>.BadRequest("Key is required").ToHttpResult();

            if (!SecretVaultScopeEnforcement.TryResolveScope(
                tenantContext,
                tenantId,
                agentId,
                userId,
                activationName,
                out var effectiveTenantId,
                out var effectiveAgentId,
                out var effectiveUserId,
                out var effectiveActivationName,
                out var forbiddenResult))
            {
                return forbiddenResult!.ToHttpResult();
            }

            var result = await service.DeleteByKeyAsync(key, effectiveTenantId, effectiveAgentId, effectiveUserId, effectiveActivationName);
            return result.ToHttpResult();
        })
        .WithName("DeleteSecretByKey")
        .WithOpenApi(o => { o.Summary = "Delete secret by key and scope"; return o; });
    }
}

public class SecretVaultCreateRequest
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
    /// <summary>When set, only this agent activation can access the secret; null = any activation of the agent can access.</summary>
    public string? ActivationName { get; set; }
    /// <summary>Optional. JSON object with string, number, or boolean values only (e.g. {"env":"prod","count":42,"enabled":true}).</summary>
    public object? AdditionalData { get; set; }
}

public class SecretVaultUpdateRequest
{
    public string Key { get; set; } = null!;
    public string? Value { get; set; }
    public string? TenantId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
    /// <summary>When set, only this agent activation can access the secret; null = any activation of the agent can access.</summary>
    public string? ActivationName { get; set; }
    /// <summary>Optional. JSON object with string, number, or boolean values only.</summary>
    public object? AdditionalData { get; set; }
}
