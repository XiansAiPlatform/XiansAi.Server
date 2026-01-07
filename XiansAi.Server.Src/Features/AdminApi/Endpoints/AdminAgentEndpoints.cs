using Features.AdminApi.Auth;
using Features.AdminApi.Configuration;
using Features.AdminApi.Utils;
using Shared.Services;
using Shared.Repositories;
using Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Data.Models;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for agent management.
/// These are administrative operations, separate from WebApi (UI) endpoints.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminAgentEndpoints
{
    /// <summary>
    /// Request model for updating an agent instance.
    /// </summary>
    public class UpdateAgentRequest
    {
        public string? Name { get; set; }
        
        public string? Description { get; set; }
        
        public Dictionary<string, object>? Configuration { get; set; }
    }

    /// <summary>
    /// Maps all AdminApi agent management endpoints.
    /// </summary>
    public static void MapAdminAgentEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminAgentGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agents")
            .WithTags("AdminAPI - Agent Management")
            .RequiresAdminApiAuth();

        // List Agent Instances
        adminAgentGroup.MapGet("", async (
            string tenantId,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !tenantId.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get tenant-scoped agents (instances) - SystemScoped = false
                var agents = await agentRepository.GetAgentsWithPermissionAsync(
                    tenantContext.LoggedInUser, 
                    tenantId);
                
                // Filter to only tenant-scoped instances (not system-scoped templates)
                var instances = agents.Where(a => !a.SystemScoped && a.Tenant == tenantId).ToList();

                // Apply pagination
                var pageNum = page ?? 1;
                var pageSizeNum = Math.Min(pageSize ?? 20, 100);
                var skip = (pageNum - 1) * pageSizeNum;
                var total = instances.Count;
                var paginated = instances.Skip(skip).Take(pageSizeNum).ToList();

                return Results.Ok(new
                {
                    agents = paginated,
                    pagination = new
                    {
                        page = pageNum,
                        pageSize = pageSizeNum,
                        totalPages = (int)Math.Ceiling(total / (double)pageSizeNum),
                        totalItems = total,
                        hasNext = skip + pageSizeNum < total,
                        hasPrevious = pageNum > 1
                    }
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error listing agents: {ex.Message}");
            }
        })
        .WithName("ListAgentInstances")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List Agent Instances",
            Description = "List all agent instances for a tenant with optional pagination."
        });

        // Get Agent Instance
        adminAgentGroup.MapGet("/{agentId}", async (
            string tenantId,
            string agentId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get agent by MongoDB ObjectId
                var agent = await agentRepository.GetByIdAsync(
                    agentId, 
                    tenantContext.LoggedInUser, 
                    tenantContext.UserRoles.ToArray());

                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !agent.Tenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                return Results.Ok(agent);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error retrieving agent: {ex.Message}");
            }
        })
        .WithName("GetAgentInstance")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Agent Instance",
            Description = "Get agent instance details by MongoDB ObjectId."
        });

        // Update Agent Instance
        adminAgentGroup.MapPatch("/{agentId}", async (
            string tenantId,
            string agentId,
            [FromBody] UpdateAgentRequest request,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get existing agent by ObjectId
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !agent.Tenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Check permissions (must be owner or admin)
                if (!agent.OwnerAccess.Contains(tenantContext.LoggedInUser) &&
                    !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) &&
                    !tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
                {
                    return Results.Forbid();
                }

                // Update fields
                if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != agent.Name)
                {
                    // Check if new name already exists
                    var nameExists = await agentRepository.ExistsAsync(request.Name, agent.Tenant);
                    if (nameExists)
                    {
                        return Results.Conflict(new { error = $"Agent with name '{request.Name}' already exists in tenant" });
                    }
                    agent.Name = request.Name;
                }

                if (request.Description != null)
                {
                    // Store description in configuration metadata
                    var config = agent.GetConfiguration() ?? new Dictionary<string, object>();
                    config["description"] = request.Description;
                    agent.SetConfiguration(config);
                }

                if (request.Configuration != null)
                {
                    var config = agent.GetConfiguration() ?? new Dictionary<string, object>();
                    foreach (var kvp in request.Configuration)
                    {
                        config[kvp.Key] = kvp.Value;
                    }
                    agent.SetConfiguration(config);
                }

                var updated = await agentRepository.UpdateAsync(
                    agent.Id, 
                    agent, 
                    tenantContext.LoggedInUser, 
                    tenantContext.UserRoles.ToArray());

                if (updated)
                {
                    return Results.Ok(agent);
                }

                return Results.Problem("Failed to update agent");
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating agent: {ex.Message}");
            }
        })
        .WithName("UpdateAgentInstance")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Agent Instance",
            Description = "Update agent instance configuration. Agent ID format: {tenantName}@{agentName}."
        });

        // Delete Agent Instance
        adminAgentGroup.MapDelete("/{agentId}", async (
            string tenantId,
            string agentId,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get existing agent by ObjectId
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent with ID '{agentId}' not found" });
                }

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !agent.Tenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Check permissions (must be owner or admin)
                if (!agent.OwnerAccess.Contains(tenantContext.LoggedInUser) &&
                    !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) &&
                    !tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
                {
                    return Results.Forbid();
                }

                var deleted = await agentRepository.DeleteAsync(
                    agent.Id, 
                    tenantContext.LoggedInUser, 
                    tenantContext.UserRoles.ToArray());

                if (deleted)
                {
                    return Results.Ok(new { message = $"Agent '{agentId}' deleted successfully" });
                }

                return Results.Problem("Failed to delete agent");
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error deleting agent: {ex.Message}");
            }
        })
        .WithName("DeleteAgentInstance")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Agent Instance",
            Description = "Delete an agent instance. Agent ID format: {tenantName}@{agentName}."
        });
    }
}

