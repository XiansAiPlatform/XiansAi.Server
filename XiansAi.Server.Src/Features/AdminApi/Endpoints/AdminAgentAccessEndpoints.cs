using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Repositories;
using System.ComponentModel.DataAnnotations;
using Features.AdminApi.Auth;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for managing an agent's owner / write / read access lists. They sit next to
/// <see cref="AdminOwnershipEndpoints"/> (which only supports viewing the lists and adding an owner)
/// and are what Agent Studio calls. This surface requires a valid Admin API key scoped to the
/// tenant; the Studio route handler performs the owner / tenant-admin gate before calling.
/// </summary>
public static class AdminAgentAccessEndpoints
{
    public class UserAccessRequest
    {
        /// <summary>The <see cref="User.UserId"/> to grant access to. Email addresses are rejected.</summary>
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public required string UserId { get; set; }

        /// <summary>One of <c>Owner</c>, <c>Write</c>, <c>Read</c> (case-insensitive).</summary>
        [Required]
        public required string Level { get; set; }
    }

    public class UserLevelRequest
    {
        [Required]
        public required string Level { get; set; }
    }

    public static void MapAdminAgentAccessEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        // Tenant-level: "which agents in this tenant can {user} edit?" Resolved by email or user id
        // so Agent Studio (which never sends an end-user token) can enforce per-agent access.
        var levelGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agent-access")
            .WithTags("AdminAPI - Agent Access")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        levelGroup.MapGet("", async (
            string tenantId,
            [FromQuery] string? user,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IUserRepository userRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (string.IsNullOrWhiteSpace(user))
            {
                return Results.BadRequest(new { error = "user query parameter is required" });
            }

            var account = await userRepository.GetByUserIdOrEmailAsync(user);
            if (account == null)
            {
                return Results.Ok(new
                {
                    user,
                    isSysAdmin = false,
                    isTenantAdmin = false,
                    agents = new Dictionary<string, string>()
                });
            }

            var isTenantAdmin = account.TenantRoles.Any(r =>
                string.Equals(r.Tenant, tenantId, StringComparison.OrdinalIgnoreCase)
                && r.IsApproved
                && r.Roles.Contains(SystemRoles.TenantAdmin));

            var allAgents = await agentRepository.GetAgentsWithPermissionAsync(
                tenantContext.LoggedInUser ?? "system", tenantId);

            var identities = new[] { account.UserId, account.Email }
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            bool OnList(List<string> list) =>
                list.Any(x => identities.Contains(x, StringComparer.OrdinalIgnoreCase));

            var agents = new Dictionary<string, string>();
            foreach (var a in allAgents.Where(a => !a.SystemScoped
                && string.Equals(a.Tenant, tenantId, StringComparison.OrdinalIgnoreCase)))
            {
                string? level = OnList(a.OwnerAccess) ? "Owner"
                    : OnList(a.WriteAccess) ? "Write"
                    : OnList(a.ReadAccess) ? "Read"
                    : null;
                if (level != null)
                {
                    agents[a.Name] = level;
                }
            }

            return Results.Ok(new
            {
                user = account.UserId,
                isSysAdmin = account.IsSysAdmin,
                isTenantAdmin,
                agents
            });
        })
        .WithName("GetTenantAgentAccessForUser")
        .WithSummary("Per-agent access level for a user within a tenant");

        var group = adminApiGroup.MapGroup("/tenants/{tenantId}/agents/{agentId}/access")
            .WithTags("AdminAPI - Agent Access")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        // Get the current access lists
        group.MapGet("", async (
            string tenantId,
            string agentId,
            [FromServices] IAgentRepository agentRepository) =>
        {
            var (agent, error) = await ResolveAgentAsync(agentRepository, agentId, tenantId);
            if (error != null) return error;

            return Results.Ok(ToAccessResponse(agent!));
        })
        .WithName("GetAgentAccess");

        // Add (or move) a user to a level
        group.MapPost("/users", async (
            string tenantId,
            string agentId,
            [FromBody] UserAccessRequest request,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IUserRepository userRepository,
            [FromServices] IWebhookEventPublisher webhookEventPublisher,
            [FromServices] ILogger<IAgentRepository> logger) =>
        {
            var (agent, error) = await ResolveAgentAsync(agentRepository, agentId, tenantId);
            if (error != null) return error;

            if (!TryParseLevel(request.Level, out var level))
            {
                return Results.BadRequest(new { error = "Invalid level. Must be one of: Owner, Write, Read" });
            }

            var userError = await ValidateTargetUserAsync(userRepository, request.UserId, agent!.Tenant!, logger);
            if (userError != null) return userError;

            ApplyUserLevel(agent, request.UserId, level);
            await agentRepository.UpdateInternalAsync(agent.Id, agent);

            await PublishAccessChangedAsync(webhookEventPublisher, agent, "user-added");
            return Results.Ok(ToAccessResponse(agent));
        })
        .WithName("AddAgentAccessUser");

        // Change a user's level
        group.MapPatch("/users/{userId}", async (
            string tenantId,
            string agentId,
            string userId,
            [FromBody] UserLevelRequest request,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IUserRepository userRepository,
            [FromServices] IWebhookEventPublisher webhookEventPublisher,
            [FromServices] ILogger<IAgentRepository> logger) =>
        {
            var (agent, error) = await ResolveAgentAsync(agentRepository, agentId, tenantId);
            if (error != null) return error;

            if (!TryParseLevel(request.Level, out var level))
            {
                return Results.BadRequest(new { error = "Invalid level. Must be one of: Owner, Write, Read" });
            }

            var userError = await ValidateTargetUserAsync(userRepository, userId, agent!.Tenant!, logger);
            if (userError != null) return userError;

            ApplyUserLevel(agent, userId, level);
            await agentRepository.UpdateInternalAsync(agent.Id, agent);

            await PublishAccessChangedAsync(webhookEventPublisher, agent, "user-updated");
            return Results.Ok(ToAccessResponse(agent));
        })
        .WithName("UpdateAgentAccessUser");

        // Remove a user from every level
        group.MapDelete("/users/{userId}", async (
            string tenantId,
            string agentId,
            string userId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IWebhookEventPublisher webhookEventPublisher) =>
        {
            var (agent, error) = await ResolveAgentAsync(agentRepository, agentId, tenantId);
            if (error != null) return error;

            agent!.RevokeOwnerAccess(userId);
            agent.RevokeWriteAccess(userId);
            agent.RevokeReadAccess(userId);
            await agentRepository.UpdateInternalAsync(agent.Id, agent);

            await PublishAccessChangedAsync(webhookEventPublisher, agent, "user-removed");
            return Results.Ok(ToAccessResponse(agent));
        })
        .WithName("RemoveAgentAccessUser");
    }

    /// <summary>
    /// Loads the agent by Mongo id and confirms it belongs to the route tenant (which
    /// <see cref="TenantRouteScopeFilter"/> has already validated against the caller's credential).
    /// Returns a 404 result on any mismatch so another tenant's agent is not disclosed.
    /// </summary>
    private static async Task<(Agent? agent, IResult? error)> ResolveAgentAsync(
        IAgentRepository agentRepository, string agentId, string tenantId)
    {
        var agent = await agentRepository.GetByIdInternalAsync(agentId);
        if (agent == null || string.IsNullOrEmpty(agent.Tenant) ||
            !string.Equals(agent.Tenant, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return (null, Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" }));
        }
        return (agent, null);
    }

    private static object ToAccessResponse(Agent agent) => new
    {
        agentId = agent.Id,
        agentName = agent.Name,
        tenantId = agent.Tenant,
        createdBy = agent.CreatedBy,
        ownerAccess = agent.OwnerAccess,
        writeAccess = agent.WriteAccess,
        readAccess = agent.ReadAccess
    };

    private static bool TryParseLevel(string? raw, out PermissionLevel level)
    {
        level = PermissionLevel.None;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var normalized = raw.Replace("Access", "", StringComparison.OrdinalIgnoreCase).Trim();
        return Enum.TryParse(normalized, ignoreCase: true, out level)
               && level is PermissionLevel.Owner or PermissionLevel.Write or PermissionLevel.Read;
    }

    private static void ApplyUserLevel(Agent agent, string userId, PermissionLevel level)
    {
        agent.RevokeOwnerAccess(userId);
        agent.RevokeWriteAccess(userId);
        agent.RevokeReadAccess(userId);

        switch (level)
        {
            case PermissionLevel.Owner: agent.GrantOwnerAccess(userId); break;
            case PermissionLevel.Write: agent.GrantWriteAccess(userId); break;
            case PermissionLevel.Read: agent.GrantReadAccess(userId); break;
        }
    }

    /// <summary>
    /// Same guarantees as <see cref="AdminOwnershipEndpoints"/>: the id must be a user id (not an
    /// address), the account must exist, must not be disabled, and must be an approved member of the
    /// agent's tenant (a system admin is exempt from the membership requirement).
    /// </summary>
    private static async Task<IResult?> ValidateTargetUserAsync(
        IUserRepository userRepository, string userId, string tenant, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.BadRequest(new { error = "userId is required" });
        }

        if (userId.Contains('@'))
        {
            return Results.BadRequest(new
            {
                error = "userId must be a user id, not an email address. " +
                        "An address can answer to more than one account."
            });
        }

        var user = await userRepository.GetByUserIdAsync(userId);
        if (user == null)
        {
            return Results.BadRequest(new { error = $"User '{userId}' not found" });
        }

        if (user.IsLockedOut)
        {
            logger.LogWarning("Agent access grant refused: {UserId} is disabled", LogSanitizer.RedactUserId(user.UserId));
            return Results.Conflict(new { error = "This account is disabled, so it cannot be given agent access. Enable it first." });
        }

        if (!user.IsSysAdmin && !IsApprovedMemberOf(user, tenant))
        {
            logger.LogWarning("Agent access grant refused: {UserId} is not a member of tenant {TenantId}",
                LogSanitizer.RedactUserId(user.UserId), LogSanitizer.Sanitize(tenant));
            return Results.BadRequest(new { error = $"User '{userId}' is not an approved member of tenant '{tenant}'" });
        }

        return null;
    }

    private static bool IsApprovedMemberOf(User user, string tenantId) =>
        user.TenantRoles.Any(membership =>
            string.Equals(membership.Tenant, tenantId, StringComparison.OrdinalIgnoreCase)
            && membership.IsApproved);

    private static async Task PublishAccessChangedAsync(
        IWebhookEventPublisher webhookEventPublisher, Agent agent, string change)
    {
        await webhookEventPublisher.PublishAsync(
            WebhookEventTypes.AgentAccessChanged,
            new
            {
                tenantId = agent.Tenant,
                agentId = agent.Id,
                agentName = agent.Name,
                change,
                ownerAccess = agent.OwnerAccess,
                writeAccess = agent.WriteAccess,
                readAccess = agent.ReadAccess
            },
            agent.Tenant);
    }
}
