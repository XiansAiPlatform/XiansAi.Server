using Microsoft.AspNetCore.Mvc;
using Shared.Repositories;
using Shared.Auth;
using System.ComponentModel.DataAnnotations;
using Features.AdminApi.Utils;
using Features.AdminApi.Auth;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils;

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
        /// <summary>
        /// The <see cref="User.UserId"/> of the account to hand ownership to. Addresses are not
        /// accepted: one can answer to more than one account, and this API knows the user id —
        /// every endpoint that returns a user returns it.
        /// </summary>
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public required string NewAdminId { get; set; }
    }

    /// <summary>
    /// Maps all AdminApi ownership endpoints.
    /// </summary>
    public static void MapAdminOwnershipEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminOwnershipGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agents/{agentId}/ownership")
            .WithTags("AdminAPI - Agent Ownership")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

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

                // GetByIdInternalAsync performs no tenant scoping. Ensure the agent belongs to the
                // caller's tenant (route tenant validated against context by TenantRouteScopeFilter)
                // to prevent cross-tenant ownership disclosure.
                if (!string.Equals(parsedTenant, tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
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
        ;

        // Transfer Ownership
        adminOwnershipGroup.MapPatch("", async (
            string tenantId,
            string agentId,
            [FromBody] TransferOwnershipRequest request,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IUserRepository userRepository,
            [FromServices] IWebhookEventPublisher webhookEventPublisher,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILogger<IUserRepository> logger) =>
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

                // GetByIdInternalAsync performs no tenant scoping. Ensure the agent belongs to the
                // caller's tenant (route tenant validated against context by TenantRouteScopeFilter)
                // to prevent transferring ownership of another tenant's agent.
                if (!string.Equals(parsedTenant, tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }

                // Check permissions (must be current owner, TenantAdmin, or SysAdmin)
                var isCurrentOwner = agent.OwnerAccess.Contains(tenantContext.LoggedInUser);
                var isSysAdmin = tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
                var isTenantAdmin = tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin);

                if (!isCurrentOwner && !isSysAdmin && !isTenantAdmin)
                {
                    return Results.Forbid();
                }

                // Ownership names exactly one account, and an address does not: two providers can
                // each hold a record for the same one. Resolving an address here would hand the
                // agent to whichever record was found rather than the one the caller meant.
                if (request.NewAdminId.Contains('@'))
                {
                    logger.LogWarning("Ownership transfer refused: {Email} is an address, not a user id",
                        LogSanitizer.RedactEmail(request.NewAdminId));
                    return Results.BadRequest(new
                    {
                        error = "newAdminId must be a user id, not an email address. " +
                                "An address can answer to more than one account."
                    });
                }

                var newAdminUser = await userRepository.GetByUserIdAsync(request.NewAdminId);
                if (newAdminUser == null)
                {
                    return Results.BadRequest(new { error = $"User '{request.NewAdminId}' not found" });
                }

                // A disabled account cannot sign in, so handing it the agent would leave that agent
                // with an owner nobody can act as.
                if (newAdminUser.IsLockedOut)
                {
                    logger.LogWarning("Ownership transfer refused: {UserId} is disabled",
                        LogSanitizer.RedactUserId(newAdminUser.UserId));
                    return Results.Conflict(new
                    {
                        error = "This account is disabled, so it cannot own an agent. Enable it first."
                    });
                }

                // The lookup above is not tenant-scoped, so without this any account in the
                // deployment could be made owner of this tenant's agent. A system administrator
                // reaches every tenant already, and is the one account that need not be a member.
                if (!newAdminUser.IsSysAdmin && !IsApprovedMemberOf(newAdminUser, parsedTenant))
                {
                    logger.LogWarning(
                        "Ownership transfer refused: {UserId} is not a member of tenant {TenantId}",
                        LogSanitizer.RedactUserId(newAdminUser.UserId), LogSanitizer.Sanitize(parsedTenant));
                    return Results.BadRequest(new
                    {
                        error = $"User '{request.NewAdminId}' is not an approved member of tenant '{parsedTenant}'"
                    });
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

                await webhookEventPublisher.PublishAsync(
                    WebhookEventTypes.AgentOwnershipTransferred,
                    new
                    {
                        tenantId = parsedTenant,
                        agentId = agent.Id,
                        agentName,
                        previousOwners,
                        newOwner = newAdminUserId,
                        transferredBy = tenantContext.LoggedInUser
                    },
                    parsedTenant);

                return Results.Ok(new
                {
                    agentId = AgentIdParser.Format(parsedTenant, agentName),
                    previousOwners,
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
        ;
    }

    /// <summary>
    /// Whether the account holds an approved membership of the tenant. A pending one is not enough:
    /// its roles do not count anywhere else either, so it names someone who cannot yet act here.
    /// </summary>
    private static bool IsApprovedMemberOf(User user, string tenantId) =>
        user.TenantRoles.Any(membership =>
            string.Equals(membership.Tenant, tenantId, StringComparison.OrdinalIgnoreCase)
            && membership.IsApproved);
}


