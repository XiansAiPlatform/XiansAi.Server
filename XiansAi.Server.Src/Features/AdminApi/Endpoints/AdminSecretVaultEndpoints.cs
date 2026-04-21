using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;
using HttpStatus = Shared.Utils.Services.StatusCode;

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
            [FromServices] ITenantContext tenantContext) =>
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
            // Strip the secret value from the response — admin must never see it.
            return RedactValue(result).ToHttpResult();
        })
        .WithName("CreateSecret")
        ;

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
        ;

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

            // Admin probe by key+scope: returns only metadata (id, key, scope, additionalData, audit).
            // The secret value is intentionally never loaded or returned for admin callers.
            var result = await service.FindMetadataByKeyAsync(key, effectiveTenantId, effectiveAgentId, effectiveUserId, effectiveActivationName);
            return result.ToHttpResult();
        })
        .WithName("FetchSecretByKey")
        ;

        group.MapGet("/{id}", async (
            string id,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Metadata-only — admin must never see the secret value.
            var result = await service.GetMetadataByIdAsync(id);
            if (!result.IsSuccess || result.Data == null)
                return result.ToHttpResult();
            if (!SecretVaultScopeEnforcement.CanAccessSecretTenant(tenantContext, result.Data.TenantId))
                return ServiceResult<SecretVaultMetadataResponse?>.Forbidden("Access denied. Secret is not in your tenant.").ToHttpResult();
            return result.ToHttpResult();
        })
        .WithName("GetSecretById")
        ;

        group.MapPut("/{id}", async (
            string id,
            [FromBody] SecretVaultUpdateRequest request,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var getResult = await service.GetMetadataByIdAsync(id);
            if (!getResult.IsSuccess || getResult.Data == null)
                return getResult.ToHttpResult();
            if (!SecretVaultScopeEnforcement.CanAccessSecretTenant(tenantContext, getResult.Data.TenantId))
                return ServiceResult<SecretVaultMetadataResponse>.Forbidden("Access denied. Secret is not in your tenant.").ToHttpResult();

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

            var actor = tenantContext.LoggedInUser ?? "system";
            var input = new SecretVaultUpdateInput(
                request.Value,
                effectiveTenantId,
                effectiveAgentId,
                effectiveUserId,
                effectiveActivationName,
                SecretVaultService.NormalizeAdditionalDataFromRequest(request.AdditionalData));
            var result = await service.UpdateAsync(id, input, actor);
            // Strip the secret value from the response — admin must never see it.
            return RedactValue(result).ToHttpResult();
        })
        .WithName("UpdateSecret")
        ;

        group.MapDelete("/{id}", async (
            string id,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var getResult = await service.GetMetadataByIdAsync(id);
            if (!getResult.IsSuccess)
                return getResult.ToHttpResult();
            if (getResult.Data != null && !SecretVaultScopeEnforcement.CanAccessSecretTenant(tenantContext, getResult.Data.TenantId))
                return ServiceResult<bool>.Forbidden("Access denied. Secret is not in your tenant.").ToHttpResult();
            var result = await service.DeleteAsync(id);
            return result.ToHttpResult();
        })
        .WithName("DeleteSecret")
        ;
    }

    /// <summary>
    /// Maps a <see cref="SecretVaultGetResponse"/>-bearing service result to the value-redacted
    /// <see cref="SecretVaultMetadataResponse"/>, preserving status code and error messages.
    /// </summary>
    private static ServiceResult<SecretVaultMetadataResponse> RedactValue(ServiceResult<SecretVaultGetResponse> source)
    {
        if (source.IsSuccess && source.Data != null)
        {
            var d = source.Data;
            var redacted = new SecretVaultMetadataResponse(
                d.Id, d.Key, d.TenantId, d.AgentId, d.UserId, d.ActivationName,
                d.AdditionalData, d.CreatedAt, d.CreatedBy, d.UpdatedAt, d.UpdatedBy);
            return ServiceResult<SecretVaultMetadataResponse>.Success(redacted, source.StatusCode);
        }

        var error = source.ErrorMessage ?? "Operation failed";
        return source.StatusCode switch
        {
            HttpStatus.BadRequest => ServiceResult<SecretVaultMetadataResponse>.BadRequest(error),
            HttpStatus.Unauthorized => ServiceResult<SecretVaultMetadataResponse>.Unauthorized(error),
            HttpStatus.Forbidden => ServiceResult<SecretVaultMetadataResponse>.Forbidden(error),
            HttpStatus.NotFound => ServiceResult<SecretVaultMetadataResponse>.NotFound(error),
            HttpStatus.Conflict => ServiceResult<SecretVaultMetadataResponse>.Conflict(error),
            _ => ServiceResult<SecretVaultMetadataResponse>.InternalServerError(error)
        };
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
    public string? Value { get; set; }
    public string? TenantId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
    /// <summary>When set, only this agent activation can access the secret; null = any activation of the agent can access.</summary>
    public string? ActivationName { get; set; }
    /// <summary>Optional. JSON object with string, number, or boolean values only.</summary>
    public object? AdditionalData { get; set; }
}
