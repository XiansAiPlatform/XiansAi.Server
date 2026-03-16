using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AgentApi.Endpoints;

public static class SecretVaultEndpoints
{
    public static void MapSecretVaultEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agent/secrets")
            .WithTags("AgentAPI - Secret Vault")
            .RequiresCertificate();

        group.MapPost("", async (
            [FromBody] AgentSecretVaultCreateRequest request,
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

            // If a tenant scope is provided/resolved, ensure the tenant exists.
            if (!string.IsNullOrWhiteSpace(effectiveTenantId))
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
        .WithName("Agent_CreateSecret")
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
        .WithName("Agent_ListSecrets")
        .WithOpenApi(o => { o.Summary = "List secrets"; o.Description = "List secrets with optional tenantId/agentId/activationName filter."; return o; });

        group.MapGet("/fetch", async (
            [FromQuery] string key,
            [FromQuery] string? tenantId,
            [FromQuery] string? agentId,
            [FromQuery] string? userId,
            [FromQuery] string? activationName,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantCacheService tenantCacheService,
            CancellationToken cancellationToken) =>
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

            // If a tenant scope is provided/resolved, ensure the tenant exists.
            if (!string.IsNullOrWhiteSpace(effectiveTenantId))
            {
                var tenant = await tenantCacheService.GetByTenantIdAsync(effectiveTenantId, cancellationToken);
                if (tenant == null)
                {
                    return ServiceResult<SecretVaultFetchResponse?>.NotFound("Tenant not found").ToHttpResult();
                }
            }

            var result = await service.FetchByKeyAsync(key, effectiveTenantId, effectiveAgentId, effectiveUserId, effectiveActivationName);
            return result.ToHttpResult();
        })
        .WithName("Agent_FetchSecretByKey")
        .WithOpenApi(o => { o.Summary = "Fetch secret by key"; o.Description = "Returns decrypted value and optional AdditionalData. Scope by tenantId, agentId, userId, activationName."; return o; });

        group.MapGet("/{id}", async (
            string id,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var result = await service.GetByIdAsync(id);
            if (!result.IsSuccess || result.Data == null)
                return result.ToHttpResult();
            if (!SecretVaultScopeEnforcement.CanAccessSecretTenant(tenantContext, result.Data.TenantId))
                return ServiceResult<SecretVaultGetResponse?>.Forbidden("Access denied. Secret is not in your tenant.").ToHttpResult();
            return result.ToHttpResult();
        })
        .WithName("Agent_GetSecretById")
        .WithOpenApi(o => { o.Summary = "Get secret by id"; return o; });

        group.MapPut("/{id}", async (
            string id,
            [FromBody] AgentSecretVaultUpdateRequest request,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantCacheService tenantCacheService,
            CancellationToken cancellationToken) =>
        {
            var getResult = await service.GetByIdAsync(id);
            if (!getResult.IsSuccess || getResult.Data == null)
                return getResult.ToHttpResult();
            if (!SecretVaultScopeEnforcement.CanAccessSecretTenant(tenantContext, getResult.Data.TenantId))
                return ServiceResult<SecretVaultGetResponse>.Forbidden("Access denied. Secret is not in your tenant.").ToHttpResult();

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

            // If a tenant scope is provided/resolved, ensure the tenant exists.
            if (!string.IsNullOrWhiteSpace(effectiveTenantId))
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
            var result = await service.UpdateAsync(id, input, actor);
            return result.ToHttpResult();
        })
        .WithName("Agent_UpdateSecret")
        .WithOpenApi(o => { o.Summary = "Update secret"; return o; });

        group.MapDelete("/{id}", async (
            string id,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var getResult = await service.GetByIdAsync(id);
            if (!getResult.IsSuccess)
                return getResult.ToHttpResult();
            if (getResult.Data != null && !SecretVaultScopeEnforcement.CanAccessSecretTenant(tenantContext, getResult.Data.TenantId))
                return ServiceResult<bool>.Forbidden("Access denied. Secret is not in your tenant.").ToHttpResult();
            var result = await service.DeleteAsync(id);
            return result.ToHttpResult();
        })
        .WithName("Agent_DeleteSecret")
        .WithOpenApi(o => { o.Summary = "Delete secret"; return o; });
    }
}

public class AgentSecretVaultCreateRequest
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
    /// <summary>When set, only this agent activation can access the secret; null = any activation of the agent can access.</summary>
    public string? ActivationName { get; set; }
    /// <summary>Optional. JSON object with string, number, or boolean values only (e.g. {"purpose":"webhook","count":1}).</summary>
    public object? AdditionalData { get; set; }
}

public class AgentSecretVaultUpdateRequest
{
    public string? Value { get; set; }
    public string? TenantId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
    /// <summary>When set, only this agent activation can access the secret; null = any activation of the agent can access.</summary>
    public string? ActivationName { get; set; }
    /// <summary>Optional. JSON object with string, number, or boolean values only.</summary>
    public object? AdditionalData { get; set; }
}
