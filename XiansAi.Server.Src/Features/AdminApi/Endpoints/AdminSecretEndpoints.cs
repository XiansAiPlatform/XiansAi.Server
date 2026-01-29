using Shared.Services;
using Shared.Data.Models;
using Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for secret vault management.
/// </summary>
public static class AdminSecretEndpoints
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

        public string? AgentId { get; set; }

        public string? UserId { get; set; }
    }

    /// <summary>
    /// Response model for secret list (without sensitive fields).
    /// </summary>
    public class SecretListItem
    {
        public required string SecretId { get; set; }
        public string? TenantId { get; set; }
        public string? AgentId { get; set; }
        public string? UserId { get; set; }
        public string? Description { get; set; }
        public required DateTime CreatedAt { get; set; }
        public required string CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// Maps all AdminApi secret endpoints.
    /// </summary>
    public static void MapAdminSecretEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminSecretGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/secrets")
            .WithTags("AdminAPI - Secret Vault")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List secrets
        adminSecretGroup.MapGet("", async (
            string tenantId,
            [FromServices] ISecretVaultService secretService,
            [FromQuery] string? agentId = null,
            [FromQuery] string? userId = null,
            [FromQuery] string? secretId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50) =>
        {
            // Normalize tenantId: "system" -> null
            var normalizedTenantId = tenantId == "system" ? null : tenantId;

            var result = await secretService.ListSecretsAsync(normalizedTenantId, agentId, userId, secretId, page, pageSize);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            var items = result.Data.items.Select(s => new SecretListItem
            {
                SecretId = s.SecretId,
                TenantId = s.TenantId,
                AgentId = s.AgentId,
                UserId = s.UserId,
                Description = s.Description,
                CreatedAt = s.CreatedAt,
                CreatedBy = s.CreatedBy,
                UpdatedAt = s.UpdatedAt,
                UpdatedBy = s.UpdatedBy
            }).ToList();

            return Results.Ok(new
            {
                items,
                totalCount = result.Data.totalCount,
                page,
                pageSize
            });
        })
        .WithName("ListSecretsForAdmin")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List secrets",
            Description = "Lists all secrets with optional filtering by agentId, userId, and secretId pattern."
        });

        // Get secret by ID
        adminSecretGroup.MapGet("/{secretId}", async (
            string tenantId,
            string secretId,
            [FromServices] ISecretVaultService secretService,
            [FromQuery] string? agentId = null,
            [FromQuery] string? userId = null,
            [FromQuery] bool includeValue = false) =>
        {
            // Normalize tenantId: "system" -> null
            var normalizedTenantId = tenantId == "system" ? null : tenantId;

            var result = await secretService.GetSecretAsync(secretId, normalizedTenantId, agentId, userId, includeValue);
            return result.ToHttpResult();
        })
        .WithName("GetSecretForAdmin")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get secret by ID",
            Description = "Retrieves a specific secret by ID. Use includeValue=true to include sensitive fields."
        });

        // Create secret
        adminSecretGroup.MapPost("", async (
            string tenantId,
            [FromServices] ISecretVaultService secretService,
            [FromServices] ITenantContext tenantContext,
            [FromBody] CreateSecretRequest request) =>
        {
            // Normalize tenantId: "system" -> null
            var normalizedTenantId = tenantId == "system" ? null : tenantId;

            var secret = new SecretData
            {
                SecretId = request.SecretId,
                TenantId = normalizedTenantId,
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
            var createdSecret = await secretService.GetSecretAsync(secret.SecretId, normalizedTenantId, secret.AgentId, secret.UserId, includeValue: false);
            return Results.Created($"/api/v1/admin/tenants/{tenantId}/secrets/{secret.SecretId}", createdSecret.Data);
        })
        .WithName("CreateSecretForAdmin")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Create secret",
            Description = "Creates a new secret in the vault."
        });

        // Update secret
        adminSecretGroup.MapPatch("/{secretId}", async (
            string tenantId,
            string secretId,
            [FromServices] ISecretVaultService secretService,
            [FromServices] ITenantContext tenantContext,
            [FromBody] UpdateSecretRequest request,
            [FromQuery] string? agentId,
            [FromQuery] string? userId) =>
        {
            // Normalize tenantId: "system" -> null
            var normalizedTenantId = tenantId == "system" ? null : tenantId;

            var updates = new SecretData
            {
                SecretId = secretId,
                // If SecretValue is not provided, use empty string as sentinel (provider will preserve existing)
                SecretValue = request.SecretValue ?? string.Empty,
                Description = request.Description,
                Metadata = request.Metadata,
                ExpireAt = request.ExpireAt,
                AgentId = request.AgentId,
                UserId = request.UserId,
                CreatedAt = DateTime.UtcNow, // Required field, will be overwritten by provider with existing value
                CreatedBy = string.Empty // Required field, will be overwritten by provider with existing value
            };

            var result = await secretService.UpdateSecretAsync(secretId, normalizedTenantId, agentId, userId, updates, tenantContext.LoggedInUser);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            // Return updated secret without sensitive fields
            var updatedSecret = await secretService.GetSecretAsync(secretId, normalizedTenantId, request.AgentId ?? agentId, request.UserId ?? userId, includeValue: false);
            return updatedSecret.ToHttpResult();
        })
        .WithName("UpdateSecretForAdmin")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update secret",
            Description = "Updates an existing secret. Only provided fields are updated."
        });

        // Delete secret
        adminSecretGroup.MapDelete("/{secretId}", async (
            string tenantId,
            string secretId,
            [FromQuery] string? agentId,
            [FromQuery] string? userId,
            [FromServices] ISecretVaultService secretService) =>
        {
            // Normalize tenantId: "system" -> null
            var normalizedTenantId = tenantId == "system" ? null : tenantId;

            var result = await secretService.DeleteSecretAsync(secretId, normalizedTenantId, agentId, userId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            return Results.Ok(new { message = $"Secret '{secretId}' deleted successfully" });
        })
        .WithName("DeleteSecretForAdmin")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete secret",
            Description = "Deletes a secret by ID."
        });
    }
}

