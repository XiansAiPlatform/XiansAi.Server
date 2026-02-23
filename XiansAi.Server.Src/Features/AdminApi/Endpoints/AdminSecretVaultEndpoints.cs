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
            [FromServices] ITenantContext tenantContext) =>
        {
            var actor = tenantContext.LoggedInUser ?? "system";
            var input = new SecretVaultCreateInput(
                request.Key,
                request.Value,
                request.TenantId,
                request.AgentId,
                request.UserId,
                SecretVaultService.NormalizeAdditionalDataFromRequest(request.AdditionalData));
            var result = await service.CreateAsync(input, actor);
            return result.ToHttpResult();
        })
        .WithName("CreateSecret")
        .WithOpenApi(o => { o.Summary = "Create secret"; o.Description = "Create a new secret. Key must be unique."; return o; });

        group.MapGet("", async (
            [FromQuery] string? tenantId,
            [FromQuery] string? agentId,
            [FromServices] ISecretVaultService service) =>
        {
            var result = await service.ListAsync(tenantId, agentId);
            return result.ToHttpResult();
        })
        .WithName("ListSecrets")
        .WithOpenApi(o => { o.Summary = "List secrets"; o.Description = "List secrets with optional tenantId/agentId filter."; return o; });

        group.MapGet("/fetch", async (
            [FromQuery] string key,
            [FromQuery] string? tenantId,
            [FromQuery] string? agentId,
            [FromQuery] string? userId,
            [FromServices] ISecretVaultService service) =>
        {
            var result = await service.FetchByKeyAsync(key, tenantId, agentId, userId);
            return result.ToHttpResult();
        })
        .WithName("FetchSecretByKey")
        .WithOpenApi(o => { o.Summary = "Fetch secret by key"; o.Description = "Returns decrypted value and optional AdditionalData. Scope by tenantId, agentId, userId."; return o; });

        group.MapGet("/{id}", async (
            string id,
            [FromServices] ISecretVaultService service) =>
        {
            var result = await service.GetByIdAsync(id);
            return result.ToHttpResult();
        })
        .WithName("GetSecretById")
        .WithOpenApi(o => { o.Summary = "Get secret by id"; return o; });

        group.MapPut("/{id}", async (
            string id,
            [FromBody] SecretVaultUpdateRequest request,
            [FromServices] ISecretVaultService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var actor = tenantContext.LoggedInUser ?? "system";
            var input = new SecretVaultUpdateInput(
                request.Value,
                request.TenantId,
                request.AgentId,
                request.UserId,
                SecretVaultService.NormalizeAdditionalDataFromRequest(request.AdditionalData));
            var result = await service.UpdateAsync(id, input, actor);
            return result.ToHttpResult();
        })
        .WithName("UpdateSecret")
        .WithOpenApi(o => { o.Summary = "Update secret"; return o; });

        group.MapDelete("/{id}", async (
            string id,
            [FromServices] ISecretVaultService service) =>
        {
            var result = await service.DeleteAsync(id);
            return result.ToHttpResult();
        })
        .WithName("DeleteSecret")
        .WithOpenApi(o => { o.Summary = "Delete secret"; return o; });
    }
}

public class SecretVaultCreateRequest
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
    /// <summary>Optional. JSON object with string, number, or boolean values only (e.g. {"env":"prod","count":42,"enabled":true}).</summary>
    public object? AdditionalData { get; set; }
}

public class SecretVaultUpdateRequest
{
    public string? Value { get; set; }
    public string? TenantId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
    /// <summary>Optional. JSON object with string, number, or boolean values only.</summary>
    public object? AdditionalData { get; set; }
}
