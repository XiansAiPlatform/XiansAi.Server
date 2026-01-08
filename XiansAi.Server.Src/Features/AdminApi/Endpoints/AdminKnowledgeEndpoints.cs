using Features.AdminApi.Constants;
using Features.AdminApi.Utils;
using Shared.Repositories;
using Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Data.Models;
using Shared.Utils;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for knowledge management.
/// Knowledge is only connected to deployed agents (in the agents collection).
/// </summary>
public static class AdminKnowledgeEndpoints
{
    /// <summary>
    /// Checks if user can modify agent resources (knowledge, tokens, etc.)
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
    /// Request model for creating knowledge.
    /// </summary>
    public class CreateKnowledgeRequest
    {
        [Required(ErrorMessage = "Name is required")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters")]
        public required string Name { get; set; }
        
        [Required(ErrorMessage = "Content is required")]
        public required string Content { get; set; }
        
        [Required(ErrorMessage = "Type is required")]
        [StringLength(50, MinimumLength = 1, ErrorMessage = "Type must be between 1 and 50 characters")]
        public required string Type { get; set; }
        
        /// <summary>
        /// Optional version identifier. If not provided, a hash will be generated.
        /// </summary>
        public string? Version { get; set; }
    }

    /// <summary>
    /// Request model for updating knowledge.
    /// </summary>
    public class UpdateKnowledgeRequest
    {
        [Required(ErrorMessage = "Content is required")]
        public required string Content { get; set; }
        
        [Required(ErrorMessage = "Type is required")]
        [StringLength(50, MinimumLength = 1, ErrorMessage = "Type must be between 1 and 50 characters")]
        public required string Type { get; set; }
        
        /// <summary>
        /// Optional version identifier. If not provided, a hash will be generated.
        /// </summary>
        public string? Version { get; set; }
    }

    /// <summary>
    /// Maps all AdminApi knowledge management endpoints.
    /// </summary>
    /// <param name="adminApiGroup">AdminAPI route group</param>
    public static void MapAdminKnowledgeEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminKnowledgeGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agents/{agentId}/knowledge")
            .WithTags("AdminAPI - Knowledge Management");

        // List all knowledge for an agent
        adminKnowledgeGroup.MapGet("", async (
            string tenantId,
            string agentId,
            [FromServices] IKnowledgeRepository knowledgeRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !tenantId.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions" },
                        statusCode: 403);
                }

                // Parse agent ID to get agent name
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.Json(new { error = "NotFound", message = $"Agent with ID '{agentId}' not found" }, statusCode: 404);
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                
                // Validate tenant matches parsed tenant
                if (!parsedTenant.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = $"Agent ID tenant '{parsedTenant}' does not match URL tenant '{tenantId}'" },
                        statusCode: 400);
                }


                // Get all knowledge for this agent
                var knowledge = await knowledgeRepository.GetUniqueLatestAsync<Knowledge>(tenantId, new List<string> { agentName });
                
                return Results.Ok(new { agentId, tenantId, knowledge });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("List Agent Knowledge")
        .WithOpenApi(operation => {
            operation.Summary = "List all knowledge for an agent";
            operation.Description = "Retrieves all knowledge items associated with a deployed agent. Only deployed agents (in the agents collection) can have knowledge.";
            return operation;
        });

        // Get specific knowledge by ID
        adminKnowledgeGroup.MapGet("/{knowledgeId}", async (
            string tenantId,
            string agentId,
            string knowledgeId,
            [FromServices] IKnowledgeRepository knowledgeRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !tenantId.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions" },
                        statusCode: 403);
                }

                // Parse agent ID to get agent name
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.Json(new { error = "NotFound", message = $"Agent with ID '{agentId}' not found" }, statusCode: 404);
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                
                // Validate tenant matches parsed tenant
                if (!parsedTenant.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = $"Agent ID tenant '{parsedTenant}' does not match URL tenant '{tenantId}'" },
                        statusCode: 400);
                }


                // Get knowledge by ID
                var knowledge = await knowledgeRepository.GetByIdAsync<Knowledge>(knowledgeId);
                if (knowledge == null)
                {
                    return Results.Json(
                        new { error = "NotFound", message = "Knowledge not found" },
                        statusCode: 404);
                }

                // Verify knowledge belongs to this agent and tenant
                if (knowledge.Agent != agentName || knowledge.TenantId != tenantId)
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Knowledge does not belong to this agent or tenant" },
                        statusCode: 403);
                }

                return Results.Ok(knowledge);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Get Knowledge by ID")
        .WithOpenApi(operation => {
            operation.Summary = "Get specific knowledge by ID";
            operation.Description = "Retrieves a specific knowledge item by its ID for a deployed agent.";
            return operation;
        });

        // Create knowledge for an agent
        adminKnowledgeGroup.MapPost("", async (
            string tenantId,
            string agentId,
            [FromBody] CreateKnowledgeRequest request,
            [FromServices] IKnowledgeRepository knowledgeRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !tenantId.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions" },
                        statusCode: 403);
                }

                // Parse agent ID to get agent name
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.Json(new { error = "NotFound", message = $"Agent with ID '{agentId}' not found" }, statusCode: 404);
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                
                // Validate tenant matches parsed tenant
                if (!parsedTenant.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = $"Agent ID tenant '{parsedTenant}' does not match URL tenant '{tenantId}'" },
                        statusCode: 400);
                }


                // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                if (!CanModifyAgentResource(tenantContext, agent))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this agent's knowledge" },
                        statusCode: 403);
                }

                // Generate version hash if not provided
                var version = request.Version;
                if (string.IsNullOrWhiteSpace(version))
                {
                    version = HashGenerator.GenerateContentHash(request.Content + request.Type);
                }

                // Check if knowledge with same content hash already exists
                var existingKnowledge = await knowledgeRepository.GetLatestByNameAsync<Knowledge>(request.Name, agentName, tenantId);
                if (existingKnowledge != null && existingKnowledge.Version == version)
                {
                    return Results.Ok(existingKnowledge);
                }

                // Create new knowledge
                var knowledge = new Knowledge
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Name = request.Name,
                    Content = request.Content,
                    Type = request.Type,
                    Version = version,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = tenantContext.LoggedInUser ?? "system",
                    TenantId = tenantId,
                    Agent = agentName
                };

                await knowledgeRepository.CreateAsync(knowledge);
                
                return Results.Created(AdminApiConstants.BuildVersionedPath($"tenants/{tenantId}/agents/{agentId}/knowledge/{knowledge.Id}"), knowledge);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Create Knowledge")
        .WithOpenApi(operation => {
            operation.Summary = "Create knowledge for an agent";
            operation.Description = "Creates a new knowledge item for a deployed agent. If a knowledge item with the same content hash already exists, returns the existing item.";
            return operation;
        });

        // Update knowledge
        adminKnowledgeGroup.MapPatch("/{knowledgeId}", async (
            string tenantId,
            string agentId,
            string knowledgeId,
            [FromBody] UpdateKnowledgeRequest request,
            [FromServices] IKnowledgeRepository knowledgeRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !tenantId.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions" },
                        statusCode: 403);
                }

                // Parse agent ID to get agent name
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.Json(new { error = "NotFound", message = $"Agent with ID '{agentId}' not found" }, statusCode: 404);
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                
                // Validate tenant matches parsed tenant
                if (!parsedTenant.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = $"Agent ID tenant '{parsedTenant}' does not match URL tenant '{tenantId}'" },
                        statusCode: 400);
                }


                // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                if (!CanModifyAgentResource(tenantContext, agent))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this agent's knowledge" },
                        statusCode: 403);
                }

                // Get existing knowledge
                var existingKnowledge = await knowledgeRepository.GetByIdAsync<Knowledge>(knowledgeId);
                if (existingKnowledge == null)
                {
                    return Results.Json(
                        new { error = "NotFound", message = "Knowledge not found" },
                        statusCode: 404);
                }

                // Verify knowledge belongs to this agent and tenant
                if (existingKnowledge.Agent != agentName || existingKnowledge.TenantId != tenantId)
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Knowledge does not belong to this agent or tenant" },
                        statusCode: 403);
                }

                // Generate version hash if not provided
                var version = request.Version;
                if (string.IsNullOrWhiteSpace(version))
                {
                    version = HashGenerator.GenerateContentHash(request.Content + request.Type);
                }

                // Update knowledge (create new version instead of updating existing)
                // This maintains version history
                var updatedKnowledge = new Knowledge
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Name = existingKnowledge.Name,
                    Content = request.Content,
                    Type = request.Type,
                    Version = version,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = tenantContext.LoggedInUser ?? "system",
                    TenantId = tenantId,
                    Agent = agentName
                };

                await knowledgeRepository.CreateAsync(updatedKnowledge);
                
                return Results.Ok(updatedKnowledge);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Update Knowledge")
        .WithOpenApi(operation => {
            operation.Summary = "Update knowledge (creates new version)";
            operation.Description = "Updates knowledge by creating a new version. This maintains version history. The old version remains in the database.";
            return operation;
        });

        // Delete knowledge by ID
        adminKnowledgeGroup.MapDelete("/{knowledgeId}", async (
            string tenantId,
            string agentId,
            string knowledgeId,
            [FromServices] IKnowledgeRepository knowledgeRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !tenantId.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions" },
                        statusCode: 403);
                }

                // Parse agent ID to get agent name
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.Json(new { error = "NotFound", message = $"Agent with ID '{agentId}' not found" }, statusCode: 404);
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                
                // Validate tenant matches parsed tenant
                if (!parsedTenant.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = $"Agent ID tenant '{parsedTenant}' does not match URL tenant '{tenantId}'" },
                        statusCode: 400);
                }


                // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                if (!CanModifyAgentResource(tenantContext, agent))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this agent's knowledge" },
                        statusCode: 403);
                }

                // Get existing knowledge
                var knowledge = await knowledgeRepository.GetByIdAsync<Knowledge>(knowledgeId);
                if (knowledge == null)
                {
                    return Results.Json(
                        new { error = "NotFound", message = "Knowledge not found" },
                        statusCode: 404);
                }

                // Verify knowledge belongs to this agent and tenant
                if (knowledge.Agent != agentName || knowledge.TenantId != tenantId)
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Knowledge does not belong to this agent or tenant" },
                        statusCode: 403);
                }

                // Delete knowledge
                var deleted = await knowledgeRepository.DeleteAsync<Knowledge>(knowledgeId);
                if (!deleted)
                {
                    return Results.Json(
                        new { error = "NotFound", message = "Knowledge not found or could not be deleted" },
                        statusCode: 404);
                }

                return Results.Ok(new { message = "Knowledge deleted successfully" });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Delete Knowledge")
        .WithOpenApi(operation => {
            operation.Summary = "Delete knowledge by ID";
            operation.Description = "Deletes a specific knowledge item by its ID.";
            return operation;
        });

        // Get versions of knowledge by name
        adminKnowledgeGroup.MapGet("/{name}/versions", async (
            string tenantId,
            string agentId,
            string name,
            [FromServices] IKnowledgeRepository knowledgeRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !tenantId.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions" },
                        statusCode: 403);
                }

                // Parse agent ID to get agent name
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.Json(new { error = "NotFound", message = $"Agent with ID '{agentId}' not found" }, statusCode: 404);
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                
                // Validate tenant matches parsed tenant
                if (!parsedTenant.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = $"Agent ID tenant '{parsedTenant}' does not match URL tenant '{tenantId}'" },
                        statusCode: 400);
                }


                // Get all versions of knowledge with this name
                var versions = await knowledgeRepository.GetByNameAsync<Knowledge>(name, agentName, tenantId);
                
                return Results.Ok(new { agentId, tenantId, name, versions });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Get Admin Knowledge Versions")
        .WithOpenApi(operation => {
            operation.Summary = "Get all versions of knowledge by name";
            operation.Description = "Retrieves all versions of a knowledge item with a specific name for an agent.";
            return operation;
        });

        // Delete all versions of knowledge by name
        adminKnowledgeGroup.MapDelete("/{name}/versions", async (
            string tenantId,
            string agentId,
            string name,
            [FromServices] IKnowledgeRepository knowledgeRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate tenant matches (unless SysAdmin)
                if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) && 
                    !tenantId.Equals(tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions" },
                        statusCode: 403);
                }

                // Parse agent ID to get agent name
                var agent = await agentRepository.GetByIdInternalAsync(agentId);
                if (agent == null)
                {
                    return Results.Json(new { error = "NotFound", message = $"Agent with ID '{agentId}' not found" }, statusCode: 404);
                }
                var parsedTenant = agent.Tenant;
                var agentName = agent.Name;
                
                // Validate tenant matches parsed tenant
                if (!parsedTenant.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = $"Agent ID tenant '{parsedTenant}' does not match URL tenant '{tenantId}'" },
                        statusCode: 400);
                }


                // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                if (!CanModifyAgentResource(tenantContext, agent))
                {
                    return Results.Json(
                        new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this agent's knowledge" },
                        statusCode: 403);
                }

                // Delete all versions
                var deleted = await knowledgeRepository.DeleteAllVersionsAsync<Knowledge>(name, agentName, tenantId);
                if (!deleted)
                {
                    return Results.Json(
                        new { error = "NotFound", message = "Knowledge not found or could not be deleted" },
                        statusCode: 404);
                }

                return Results.Ok(new { message = "All versions deleted successfully" });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Delete All Knowledge Versions")
        .WithOpenApi(operation => {
            operation.Summary = "Delete all versions of knowledge by name";
            operation.Description = "Deletes all versions of a knowledge item with a specific name for an agent.";
            return operation;
        });
    }
}

