using Features.AdminApi.Auth;
using Features.AdminApi.Utils;
using Microsoft.AspNetCore.Mvc;
using Shared.Repositories;
using Shared.Auth;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

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
    public static void MapAdminOwnershipEndpoints(this WebApplication app)
    {
        var adminOwnershipGroup = app.MapGroup("/api/admin/tenants/{tenantId}/agents/{agentId}/ownership")
            .WithTags("AdminAPI - Agent Ownership")
            .RequiresAdminApiAuth();

        // Get Ownership Information
        adminOwnershipGroup.MapGet("", async (
            string tenantId,
            string agentId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Parse agent ID
                var (parsedTenant, agentName) = AgentIdParser.Parse(agentId, tenantId);

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get agent
                var agent = await agentRepository.GetByNameAsync(
                    agentName, 
                    parsedTenant, 
                    tenantContext.LoggedInUser, 
                    tenantContext.UserRoles.ToArray());

                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent '{agentId}' not found" });
                }

                var adminId = agent.GetAdminId();
                var deployedById = agent.GetDeployedById();
                var deployedAt = agent.GetDeployedAt();
                var reportingUsers = agent.GetReportingUsers();

                return Results.Ok(new
                {
                    agentId = AgentIdParser.Format(parsedTenant, agentName),
                    adminId = adminId,
                    tenantId = parsedTenant,
                    createdBy = agent.CreatedBy,
                    deployedBy = deployedById,
                    deployedAt = deployedAt,
                    createdAt = agent.CreatedAt,
                    permissions = new
                    {
                        admin = new
                        {
                            userId = adminId,
                            grantedAt = agent.CreatedAt
                        },
                        reportingUsers = reportingUsers.Select(userId => new
                        {
                            userId = userId,
                            addedAt = agent.CreatedAt // TODO: Track when each user was added
                        }).ToList()
                    }
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
            Description = "Get ownership information for an agent instance, including admin ID and deployment details."
        });

        // Transfer Ownership
        adminOwnershipGroup.MapPut("", async (
            string tenantId,
            string agentId,
            [FromBody] TransferOwnershipRequest request,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IUserRepository userRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Parse agent ID
                var (parsedTenant, agentName) = AgentIdParser.Parse(agentId, tenantId);

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get agent
                var agent = await agentRepository.GetByNameInternalAsync(agentName, parsedTenant);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent '{agentId}' not found" });
                }

                // Check permissions (must be current admin or SysAdmin)
                var currentAdminId = agent.GetAdminId();
                var isCurrentAdmin = (currentAdminId != null && currentAdminId.Equals(tenantContext.LoggedInUser, StringComparison.OrdinalIgnoreCase)) ||
                                    agent.OwnerAccess.Contains(tenantContext.LoggedInUser);
                var isSysAdmin = tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);

                if (!isCurrentAdmin && !isSysAdmin)
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

                // Determine the identifier to use (prefer email if available)
                var newAdminIdentifier = newAdminUser.Email ?? newAdminUser.UserId;

                // Store previous admin
                var previousAdminId = currentAdminId;

                // Update admin in metadata
                agent.SetAdminId(newAdminIdentifier);

                // Grant owner access to new admin (by userId)
                if (!agent.OwnerAccess.Contains(newAdminUser.UserId))
                {
                    agent.OwnerAccess.Add(newAdminUser.UserId);
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
                    previousAdminId = previousAdminId,
                    newAdminId = newAdminIdentifier,
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
            Description = "Transfer agent instance ownership to a new admin user (by user identifier - userId, email, username, etc.)."
        });
    }
}

