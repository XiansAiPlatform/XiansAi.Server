using Features.AdminApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Repositories;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for managing agent tokens (for external integrations like webhooks).
/// </summary>
public static class AdminTokenEndpoints
{
    /// <summary>
    /// Request model for issuing a token.
    /// </summary>
    public class IssueTokenRequest
    {
        public string? Description { get; set; }
        
        public DateTime? ExpiresAt { get; set; }
        
        public List<string>? Scopes { get; set; }
    }

    /// <summary>
    /// Maps all AdminApi token endpoints.
    /// </summary>
    public static void MapAdminTokenEndpoints(this WebApplication app)
    {
        var adminTokenGroup = app.MapGroup("/api/admin/tenants/{tenantId}/agents/{agentId}/tokens")
            .WithTags("AdminAPI - Agent Tokens")
            .RequiresAdminApiAuth();

        // Issue Token
        adminTokenGroup.MapPost("", async (
            string tenantId,
            string agentId,
            [FromBody] IssueTokenRequest? request,
            [FromServices] IAgentRepository agentRepository) =>
        {
            // TODO: Implement token issuance
            // Generate token for external parties to call the agent (e.g., webhooks)
            return Results.StatusCode(501);
        })
        .WithName("IssueToken")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Issue Agent Token",
            Description = "Issue a token for external parties to call the agent (e.g., webhooks)."
        });

        // List Tokens
        adminTokenGroup.MapGet("", async (
            string tenantId,
            string agentId,
            [FromServices] IAgentRepository agentRepository) =>
        {
            // TODO: Implement token listing
            return Results.StatusCode(501);
        })
        .WithName("ListTokens")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List Agent Tokens",
            Description = "List all tokens issued for an agent instance."
        });

        // Get Token Details
        adminTokenGroup.MapGet("/{tokenId}", async (
            string tenantId,
            string agentId,
            string tokenId,
            [FromServices] IAgentRepository agentRepository) =>
        {
            // TODO: Implement token details retrieval
            return Results.StatusCode(501);
        })
        .WithName("GetTokenDetails")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Token Details",
            Description = "Get details for a specific agent token."
        });

        // Revoke Token
        adminTokenGroup.MapDelete("/{tokenId}", async (
            string tenantId,
            string agentId,
            string tokenId,
            [FromServices] IAgentRepository agentRepository) =>
        {
            // TODO: Implement token revocation
            return Results.StatusCode(501);
        })
        .WithName("RevokeToken")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Revoke Agent Token",
            Description = "Revoke an agent token, preventing further use."
        });
    }
}

