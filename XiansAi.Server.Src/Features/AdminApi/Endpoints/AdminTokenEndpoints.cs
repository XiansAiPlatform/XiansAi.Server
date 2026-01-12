using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data;
using Shared.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for managing agent tokens (for external integrations like webhooks).
/// </summary>
public static class AdminTokenEndpoints
{
    /// <summary>
    /// Checks if user can modify agent resources (tokens, knowledge, etc.)
    /// </summary>
    private static bool CanModifyAgentResource(ITenantContext tenantContext, Agent agent)
    {
        // System Admin can modify anything
        if (tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            return true;
        
        // Tenant Admin can modify all agents in their tenant
        if (tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
            return true;
        
        // Regular users can only modify agents they own
        return agent.OwnerAccess.Contains(tenantContext.LoggedInUser);
    }
    /// <summary>
    /// Token model stored in MongoDB
    /// </summary>
    public class AgentToken
    {
        public ObjectId Id { get; set; }
        public required string TokenId { get; set; }          // Public ID (e.g., "tok_abc123")
        public required string AgentId { get; set; }          // Format: {tenantId}@{agentName}
        public required string TenantId { get; set; }
        public required string TokenHash { get; set; }        // SHA256 hash of token
        public string? Description { get; set; }
        public List<string>? Scopes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public required string CreatedBy { get; set; }        // User who created token
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Request model for issuing a token.
    /// </summary>
    public class IssueTokenRequest
    {
        [StringLength(500)]
        public string? Description { get; set; }
        
        public DateTime? ExpiresAt { get; set; }
        
        public List<string>? Scopes { get; set; }
    }

    /// <summary>
    /// Response when token is issued (token value only shown once)
    /// </summary>
    public class IssueTokenResponse
    {
        public required string TokenId { get; set; }
        public required string Token { get; set; }            // Full token (only shown once!)
        public required string AgentId { get; set; }
        public required string TenantId { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public List<string>? Scopes { get; set; }
    }

    /// <summary>
    /// Token list item (token value never exposed)
    /// </summary>
    public class TokenListItem
    {
        public required string TokenId { get; set; }
        public string? TokenPreview { get; set; }             // e.g., "xai_***...xyz"
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; }
        public List<string>? Scopes { get; set; }
    }

    /// <summary>
    /// Generates a cryptographically secure token
    /// </summary>
    private static string GenerateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var base64 = Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return $"xai_live_{base64.Substring(0, 43)}";  // 43 chars after prefix
    }

    /// <summary>
    /// Hashes a token using SHA256
    /// </summary>
    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Creates a token preview (e.g., "xai_***...xyz")
    /// </summary>
    private static string CreateTokenPreview(string tokenId)
    {
        // Don't expose any part of actual token, just a safe preview
        return $"xai_***...{tokenId.Substring(Math.Max(0, tokenId.Length - 4))}";
    }

    /// <summary>
    /// Maps all AdminApi token endpoints.
    /// </summary>
    public static void MapAdminTokenEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminTokenGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agents/{agentId}/tokens")
            .WithTags("AdminAPI - Agent Tokens")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Issue Token
        adminTokenGroup.MapPost("", async (
            string tenantId,
            string agentId,
            [FromBody] IssueTokenRequest? request,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IMongoDatabase db,
            [FromServices] ITenantContext tenantContext,
            HttpContext httpContext) =>
        {
            try
            {
                // Parse agent ID
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                var fullAgentId = $"{parsedTenant}@{agentName}";

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }


                // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                if (!CanModifyAgentResource(tenantContext, agent))
                {
                    return Results.Forbid();
                }

                // Validate expiration date
                if (request?.ExpiresAt.HasValue == true && request.ExpiresAt.Value < DateTime.UtcNow)
                {
                    return Results.BadRequest(new { error = "Expiration date must be in the future" });
                }

                // Generate token
                var token = GenerateToken();
                var tokenHash = HashToken(token);
                var tokenId = $"tok_{Guid.NewGuid().ToString("N").Substring(0, 16)}";

                // Create token record
                var agentToken = new AgentToken
                {
                    TokenId = tokenId,
                    AgentId = fullAgentId,
                    TenantId = parsedTenant,
                    TokenHash = tokenHash,
                    Description = request?.Description,
                    Scopes = request?.Scopes,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = request?.ExpiresAt,
                    CreatedBy = tenantContext.LoggedInUser,
                    IsActive = true
                };

                // Store in MongoDB
                var tokensCollection = db.GetCollection<AgentToken>("agent_tokens");
                await tokensCollection.InsertOneAsync(agentToken);

                // Return token (ONLY TIME IT'S SHOWN!)
                return Results.Ok(new IssueTokenResponse
                {
                    TokenId = tokenId,
                    Token = token,
                    AgentId = fullAgentId,
                    TenantId = parsedTenant,
                    Description = request?.Description,
                    CreatedAt = agentToken.CreatedAt,
                    ExpiresAt = request?.ExpiresAt,
                    Scopes = request?.Scopes
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error issuing token"
                );
            }
        })
        .WithName("IssueToken")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Issue Agent Token",
            Description = "Issue a token for external parties to call the agent (e.g., webhooks). Token value is only shown once upon creation."
        });

        // List Tokens
        adminTokenGroup.MapGet("", async (
            string tenantId,
            string agentId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IMongoDatabase db,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Parse agent ID
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                var fullAgentId = $"{parsedTenant}@{agentName}";

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get tokens from MongoDB
                var tokensCollection = db.GetCollection<AgentToken>("agent_tokens");
                var tokens = await tokensCollection
                    .Find(t => t.AgentId == fullAgentId && t.IsActive)
                    .SortByDescending(t => t.CreatedAt)
                    .ToListAsync();

                // Map to list items (never expose token value)
                var tokenList = tokens.Select(t => new TokenListItem
                {
                    TokenId = t.TokenId,
                    TokenPreview = CreateTokenPreview(t.TokenId),
                    Description = t.Description,
                    CreatedAt = t.CreatedAt,
                    LastUsedAt = t.LastUsedAt,
                    ExpiresAt = t.ExpiresAt,
                    IsActive = t.IsActive && (!t.ExpiresAt.HasValue || t.ExpiresAt.Value > DateTime.UtcNow),
                    Scopes = t.Scopes
                }).ToList();

                return Results.Ok(tokenList);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error listing tokens"
                );
            }
        })
        .WithName("ListTokens")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List Agent Tokens",
            Description = "List all tokens issued for an agent instance. Token values are never exposed, only metadata."
        });

        // Get Token Details
        adminTokenGroup.MapGet("/{tokenId}", async (
            string tenantId,
            string agentId,
            string tokenId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IMongoDatabase db,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Parse agent ID
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                var fullAgentId = $"{parsedTenant}@{agentName}";

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get token from MongoDB
                var tokensCollection = db.GetCollection<AgentToken>("agent_tokens");
                var token = await tokensCollection
                    .Find(t => t.TokenId == tokenId && t.AgentId == fullAgentId)
                    .FirstOrDefaultAsync();

                if (token == null)
                {
                    return Results.NotFound(new { error = "Token not found" });
                }

                // Return token details (never expose token value)
                var tokenDetails = new TokenListItem
                {
                    TokenId = token.TokenId,
                    TokenPreview = CreateTokenPreview(token.TokenId),
                    Description = token.Description,
                    CreatedAt = token.CreatedAt,
                    LastUsedAt = token.LastUsedAt,
                    ExpiresAt = token.ExpiresAt,
                    IsActive = token.IsActive && (!token.ExpiresAt.HasValue || token.ExpiresAt.Value > DateTime.UtcNow),
                    Scopes = token.Scopes
                };

                return Results.Ok(tokenDetails);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error getting token details"
                );
            }
        })
        .WithName("GetTokenDetails")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Token Details",
            Description = "Get details for a specific agent token. Token value is never exposed."
        });

        // Revoke Token
        adminTokenGroup.MapDelete("/{tokenId}", async (
            string tenantId,
            string agentId,
            string tokenId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IMongoDatabase db,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Parse agent ID
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                var fullAgentId = $"{parsedTenant}@{agentName}";

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                if (!CanModifyAgentResource(tenantContext, agent))
                {
                    return Results.Forbid();
                }

                // Revoke token (soft delete - set IsActive = false)
                var tokensCollection = db.GetCollection<AgentToken>("agent_tokens");
                var updateResult = await tokensCollection.UpdateOneAsync(
                    t => t.TokenId == tokenId && t.AgentId == fullAgentId,
                    Builders<AgentToken>.Update.Set(t => t.IsActive, false)
                );

                if (updateResult.MatchedCount == 0)
                {
                    return Results.NotFound(new { error = "Token not found" });
                }

                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error revoking token"
                );
            }
        })
        .WithName("RevokeToken")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Revoke Agent Token",
            Description = "Revoke an agent token, preventing further use. Token is soft-deleted (IsActive = false)."
        });
    }
}
