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
    /// Request model for creating/deploying an agent instance.
    /// </summary>
    public class CreateAgentRequest
    {
        [Required]
        public required string Name { get; set; }
        
        public string? Description { get; set; }
        
        public string? Category { get; set; }
        
        public Dictionary<string, object>? Configuration { get; set; }
        
        public List<ReportingTo>? ReportingTargets { get; set; }
        
        // Legacy field for backward compatibility
        [Obsolete("Use ReportingTargets instead")]
        public List<string>? ReportingUsers { get; set; }
        
        /// <summary>
        /// Agent template ID (required if deploying from template).
        /// </summary>
        public string? TemplateId { get; set; }
        
        /// <summary>
        /// If true, deploy from agent template using TemplateId.
        /// </summary>
        public bool FromTemplate { get; set; } = false;
    }

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

        // Create/Deploy Agent Instance
        adminAgentGroup.MapPost("", async (
            string tenantId,
            [FromBody] CreateAgentRequest request,
            [FromServices] ITemplateService templateService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IUserRepository userRepository,
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

                // If deploying from template
                if (request.FromTemplate && !string.IsNullOrWhiteSpace(request.TemplateId))
                {
                    var deployResult = await templateService.DeployTemplateToTenant(
                        request.TemplateId, 
                        tenantId, 
                        tenantContext.LoggedInUser, 
                        null);
                    
                    if (!deployResult.IsSuccess)
                    {
                        return deployResult.ToHttpResult();
                    }

                    var agent = deployResult.Data!;
                    
                    // Add reporting targets if provided
                    if (request.ReportingTargets != null && request.ReportingTargets.Count > 0)
                    {
                        foreach (var target in request.ReportingTargets)
                        {
                            if (target != null && !string.IsNullOrWhiteSpace(target.Id))
                            {
                                target.AddedAt = DateTime.UtcNow;
                                target.AddedBy = tenantContext.LoggedInUser;
                                await agentRepository.AddReportingTargetAsync(agent.Id, target);
                            }
                        }
                    }
                    // Legacy: Support old ReportingUsers field for backward compatibility
                    else if (request.ReportingUsers != null && request.ReportingUsers.Count > 0)
                    {
                        foreach (var userId in request.ReportingUsers)
                        {
                            if (!string.IsNullOrWhiteSpace(userId))
                            {
                                var target = new ReportingTo 
                                { 
                                    Id = userId, 
                                    Attributes = new Dictionary<string, string> { { "type", "user" } },
                                    AddedAt = DateTime.UtcNow,
                                    AddedBy = tenantContext.LoggedInUser
                                };
                                await agentRepository.AddReportingTargetAsync(agent.Id, target);
                            }
                        }
                    }

                    // Update configuration if provided
                    if (request.Configuration != null && request.Configuration.Count > 0)
                    {
                        agent.SetConfiguration(request.Configuration);
                        await agentRepository.UpdateInternalAsync(agent.Id, agent);
                    }

                    return Results.Created($"{AdminApiConfiguration.GetBasePath()}/tenants/{tenantId}/agents/{tenantId}@{agent.Name}", agent);
                }
                else
                {
                    // Create new agent instance directly
                    // Get user identifier (prefer email if available, fallback to userId)
                    var user = await userRepository.GetByUserIdAsync(tenantContext.LoggedInUser);
                    var adminIdentifier = user?.Email ?? tenantContext.LoggedInUser;

                    // Check if agent already exists
                    var exists = await agentRepository.ExistsAsync(request.Name, tenantId);
                    if (exists)
                    {
                        return Results.Conflict(new { error = $"Agent '{request.Name}' already exists in tenant '{tenantId}'" });
                    }

                    var newAgent = new Agent
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Name = request.Name,
                        Tenant = tenantId,
                        SystemScoped = false, // Tenant-scoped instance
                        AgentTemplateId = null, // Not from template
                        CreatedBy = tenantContext.LoggedInUser,
                        CreatedAt = DateTime.UtcNow,
                        OwnerAccess = new List<string> { tenantContext.LoggedInUser },
                        ReadAccess = new List<string>(),
                        WriteAccess = new List<string>(),
                        Metadata = new Dictionary<string, object>()
                    };

                    // Store instance-specific data in metadata
                    newAgent.SetAdminId(adminIdentifier);
                    newAgent.SetDeployedAt(DateTime.UtcNow);
                    newAgent.SetDeployedById(adminIdentifier);
                    // Set reporting targets
                    if (request.ReportingTargets != null && request.ReportingTargets.Count > 0)
                    {
                        foreach (var target in request.ReportingTargets)
                        {
                            if (target != null && !string.IsNullOrWhiteSpace(target.Id))
                            {
                                target.AddedAt = DateTime.UtcNow;
                                target.AddedBy = tenantContext.LoggedInUser;
                            }
                        }
                        newAgent.SetReportingTargets(request.ReportingTargets);
                    }
                    // Legacy: Support old ReportingUsers field
                    else if (request.ReportingUsers != null && request.ReportingUsers.Count > 0)
                    {
                        var targets = request.ReportingUsers
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .Select(userId => new ReportingTo 
                            { 
                                Id = userId, 
                                Attributes = new Dictionary<string, string> { { "type", "user" } },
                                AddedAt = DateTime.UtcNow,
                                AddedBy = tenantContext.LoggedInUser
                            })
                            .ToList();
                        newAgent.SetReportingTargets(targets);
                    }
                    else
                    {
                        newAgent.SetReportingTargets(new List<ReportingTo>());
                    }
                    if (request.Configuration != null)
                    {
                        newAgent.SetConfiguration(request.Configuration);
                    }

                    await agentRepository.CreateAsync(newAgent);
                    return Results.Created($"{AdminApiConfiguration.GetBasePath()}/tenants/{tenantId}/agents/{tenantId}@{newAgent.Name}", newAgent);
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating agent: {ex.Message}");
            }
        })
        .WithName("CreateAgentInstance")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Create Agent Instance / Deploy Agent Template",
            Description = "Create a new agent instance in a tenant or deploy an agent template as an agent instance. The authenticated user becomes the admin of the agent instance."
        });

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
                // Parse agent ID
                var (parsedTenant, agentName) = AgentIdParser.Parse(agentId, tenantId);

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get agent from agents collection
                var agent = await agentRepository.GetByNameAsync(
                    agentName, 
                    parsedTenant, 
                    tenantContext.LoggedInUser, 
                    tenantContext.UserRoles.ToArray());

                if (agent != null)
                {
                    return Results.Ok(agent);
                }

                return Results.NotFound(new { error = $"Agent '{agentId}' not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
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
            Description = "Get agent instance details by ID. Agent ID format: {tenantName}@{agentName} or just {agentName}."
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
                // Parse agent ID
                var (parsedTenant, agentName) = AgentIdParser.Parse(agentId, tenantId);

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get existing agent
                var agent = await agentRepository.GetByNameInternalAsync(agentName, parsedTenant);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent '{agentId}' not found" });
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
                    var nameExists = await agentRepository.ExistsAsync(request.Name, parsedTenant);
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
                // Parse agent ID
                var (parsedTenant, agentName) = AgentIdParser.Parse(agentId, tenantId);

                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !parsedTenant.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Forbid();
                }

                // Get existing agent
                var agent = await agentRepository.GetByNameInternalAsync(agentName, parsedTenant);
                if (agent == null)
                {
                    return Results.NotFound(new { error = $"Agent '{agentId}' not found" });
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

