using Shared.Services;
using Shared.Data.Models;
using Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Shared.Utils.Services;
using Features.AgentApi.Auth;

namespace Features.AgentApi.Endpoints;

/// <summary>
/// AgentApi endpoints for secret vault access.
/// Secrets are automatically scoped based on the agent's context (tenant from certificate).
/// </summary>
public static class SecretEndpoints
{
    /// <summary>
    /// Request model for creating a secret.
    /// </summary>
    public class CreateSecretRequest
    {
        [Required(ErrorMessage = "secretId is required")]
        public required string SecretId { get; set; }

        public string? AgentId { get; set; }

        public string? UserId { get; set; }

        [Required(ErrorMessage = "secretValue is required")]
        public required string SecretValue { get; set; }

        public string? Description { get; set; }

        public string? Metadata { get; set; }

        public DateTime? ExpireAt { get; set; }
    }

    /// <summary>
    /// Request model for updating a secret.
    /// </summary>
    public class UpdateSecretRequest
    {
        public string? SecretValue { get; set; }

        public string? Description { get; set; }

        public string? Metadata { get; set; }

        public DateTime? ExpireAt { get; set; }
    }

    /// <summary>
    /// Response model for secret list (without sensitive fields).
    /// </summary>
    public class SecretListItem
    {
        public required string SecretId { get; set; }
        public string? Description { get; set; }
        public required DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Maps all AgentApi secret endpoints.
    /// </summary>
    public static void MapSecretEndpoints(this WebApplication app)
    {
        var secretGroup = app.MapGroup("/api/agent/secrets")
            .WithTags("AgentAPI - Secret Vault")
            .RequiresCertificate();

        // Get secret by ID
        secretGroup.MapGet("/{secretId}", async (
            string secretId,
            [FromQuery] string? agentId,
            [FromQuery] string? userId,
            [FromServices] ISecretVaultService secretService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Resolve scopes from agent context
            var tenantId = tenantContext.TenantId;

            var result = await secretService.GetSecretAsync(secretId, tenantId, agentId, userId, includeValue: true);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            // Return secret with all fields (AgentApi always includes values)
            return Results.Ok(result.Data);
        })
        .WithName("GetSecret")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get secret by ID",
            Description = "Retrieves a secret by its secretId. The system automatically resolves scopes based on the agent's context."
        });

        // List secrets
        secretGroup.MapGet("", async (
            [FromServices] ISecretVaultService secretService,
            [FromServices] ITenantContext tenantContext,
            [FromQuery] string? agentId = null,
            [FromQuery] string? userId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50) =>
        {
            // Resolve scopes from agent context
            var tenantId = tenantContext.TenantId;

            var result = await secretService.ListSecretsAsync(tenantId, agentId, userId, null, page, pageSize);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            var items = result.Data.items.Select(s => new SecretListItem
            {
                SecretId = s.SecretId,
                Description = s.Description,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            }).ToList();

            return Results.Ok(new
            {
                items,
                totalCount = result.Data.totalCount,
                page,
                pageSize
            });
        })
        .WithName("ListSecrets")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List secrets",
            Description = "Lists all secrets accessible to the agent. The system automatically resolves scopes based on the agent's context."
        });

        // Create secret
        secretGroup.MapPost("", async (
            [FromServices] ISecretVaultService secretService,
            [FromServices] ITenantContext tenantContext,
            [FromBody] CreateSecretRequest request) =>
        {
            // Resolve scopes from agent context
            var tenantId = tenantContext.TenantId;

            var secret = new SecretData
            {
                SecretId = request.SecretId,
                TenantId = tenantId,
                AgentId = request.AgentId,
                UserId = request.UserId,
                SecretValue = request.SecretValue,
                Description = request.Description,
                Metadata = request.Metadata,
                ExpireAt = request.ExpireAt,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = tenantContext.LoggedInUser
            };

            var result = await secretService.CreateSecretAsync(secret, tenantContext.LoggedInUser);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            // Return created secret without sensitive fields
            var createdSecret = await secretService.GetSecretAsync(secret.SecretId, tenantId, secret.AgentId, secret.UserId, includeValue: false);
            return Results.Created($"/api/agent/secrets/{secret.SecretId}", createdSecret.Data);
        })
        .WithName("CreateSecret")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Create secret",
            Description = "Creates a new secret. The system automatically resolves scopes based on the agent's context."
        });

        // Update secret
        secretGroup.MapPatch("/{secretId}", async (
            string secretId,
            [FromServices] ISecretVaultService secretService,
            [FromServices] ITenantContext tenantContext,
            [FromBody] UpdateSecretRequest request,
            [FromQuery] string? agentId,
            [FromQuery] string? userId) =>
        {
            // Resolve scopes from agent context
            var tenantId = tenantContext.TenantId;

            var updates = new SecretData
            {
                SecretId = secretId,
                // If SecretValue is not provided, use empty string as sentinel (provider will preserve existing)
                SecretValue = request.SecretValue ?? string.Empty,
                Description = request.Description,
                Metadata = request.Metadata,
                ExpireAt = request.ExpireAt,
                CreatedAt = DateTime.UtcNow, // Required field, will be overwritten by provider with existing value
                CreatedBy = string.Empty // Required field, will be overwritten by provider with existing value
            };

            var result = await secretService.UpdateSecretAsync(secretId, tenantId, agentId, userId, updates, tenantContext.LoggedInUser);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            // Return updated secret without sensitive fields
            var updatedSecret = await secretService.GetSecretAsync(secretId, tenantId, agentId, userId, includeValue: false);
            return updatedSecret.ToHttpResult();
        })
        .WithName("UpdateSecret")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update secret",
            Description = "Updates an existing secret. Only provided fields are updated. The system automatically resolves scopes based on the agent's context."
        });

        // Delete secret
        secretGroup.MapDelete("/{secretId}", async (
            string secretId,
            [FromQuery] string? agentId,
            [FromQuery] string? userId,
            [FromServices] ISecretVaultService secretService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Resolve scopes from agent context
            var tenantId = tenantContext.TenantId;

            var result = await secretService.DeleteSecretAsync(secretId, tenantId, agentId, userId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            return Results.Ok(new { message = $"Secret '{secretId}' deleted successfully" });
        })
        .WithName("DeleteSecret")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete secret",
            Description = "Deletes a secret by ID. The system automatically resolves scopes based on the agent's context."
        });
    }
}

