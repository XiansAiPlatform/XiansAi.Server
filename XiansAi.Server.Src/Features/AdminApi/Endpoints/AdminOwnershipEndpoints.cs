using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Repositories;
using Shared.Auth;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using Features.AdminApi.Utils;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for managing agent instance ownership.
/// </summary>
public static class AdminOwnershipEndpoints
{
    /// <summary>
    /// Request model for transferring ownership.
    /// </summary>
    public class TransferOwnershipRequest
    {
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public required string NewAdminId { get; set; }  // User identifier (userId, email, username, etc.)
    }

    /// <summary>
    /// Maps all AdminApi ownership endpoints.
    /// </summary>
    public static void MapAdminOwnershipEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminOwnershipGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agents/{agentId}/ownership")
            .WithTags("AdminAPI - Agent Ownership")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Get Ownership Information
        adminOwnershipGroup.MapGet("", async (
            string tenantId,
            string agentId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get agent by ObjectId
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;

                // Validate parsedTenant is not null
                if (string.IsNullOrEmpty(parsedTenant))
                {
                    return Results.BadRequest(new { error = "Agent tenant is not set" });
                }

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                return Results.Ok(new
                {
                    agentId = AgentIdParser.Format(parsedTenant, agentName),
                    tenantId = parsedTenant,
                    createdBy = agent.CreatedBy,
                    createdAt = agent.CreatedAt,
                    ownerAccess = agent.OwnerAccess,
                    readAccess = agent.ReadAccess,
                    writeAccess = agent.WriteAccess
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving ownership: {ex.Message}");
            }
        })
        .WithName("GetOwnership")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Ownership Information",
            Description = "Get ownership information for an agent instance, including access lists."
        });

        // Transfer Ownership
        adminOwnershipGroup.MapPatch("", async (
            string tenantId,
            string agentId,
            [FromBody] TransferOwnershipRequest request,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IUserRepository userRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get agent by ObjectId
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;

                // Validate parsedTenant is not null
                if (string.IsNullOrEmpty(parsedTenant))
                {
                    return Results.BadRequest(new { error = "Agent tenant is not set" });
                }

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Check permissions (must be current owner, TenantAdmin, or SysAdmin)
                var isCurrentOwner = agent.OwnerAccess.Contains(tenantContext.LoggedInUser);
                var isSysAdmin = tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
                var isTenantAdmin = tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin);

                if (!isCurrentOwner && !isSysAdmin && !isTenantAdmin)
                {
                    return Results.Forbid();
                }

                // Get new admin user to validate they exist
                // Try by userId first, then by email
                var newAdminUser = await userRepository.GetByUserIdAsync(request.NewAdminId) ??
                                   await userRepository.GetByUserEmailAsync(request.NewAdminId);

                if (newAdminUser == null)
                {
                    return Results.BadRequest(new { error = $"User '{request.NewAdminId}' not found" });
                }

                // Determine the identifier to use (prefer userId)
                var newAdminUserId = newAdminUser.UserId;

                // Store previous owners
                var previousOwners = new List<string>(agent.OwnerAccess);

                // Grant owner access to new admin (by userId)
                if (!agent.OwnerAccess.Contains(newAdminUserId))
                {
                    agent.OwnerAccess.Add(newAdminUserId);
                }

                // Update the agent
                var updated = await agentRepository.UpdateInternalAsync(agent.Id, agent);
                if (!updated)
                {
                    return Results.Problem("Failed to transfer ownership");
                }

                return Results.Ok(new
                {
                    agentId = AgentIdParser.Format(parsedTenant, agentName),
                    previousOwners = previousOwners,
                    newOwner = newAdminUserId,
                    ownerAccess = agent.OwnerAccess,
                    transferredAt = DateTime.UtcNow,
                    transferredBy = tenantContext.LoggedInUser
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error transferring ownership: {ex.Message}");
            }
        })
        .WithName("TransferOwnership")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Transfer Ownership",
            Description = "Transfer agent instance ownership by adding a new user to the OwnerAccess list."
        });
    }
}


