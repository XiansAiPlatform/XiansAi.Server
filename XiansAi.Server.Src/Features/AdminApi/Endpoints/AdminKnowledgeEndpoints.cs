using Features.AdminApi.Constants;
using Shared.Repositories;
using Shared.Services;
using Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

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
        var adminKnowledgeGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/knowledge")
            .WithTags("AdminAPI - Knowledge Management")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List all knowledge for a tenant (optionally filtered by agentName or activationName)
        adminKnowledgeGroup.MapGet("", async (
            string tenantId,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                List<string>? agentNames = null;

                // If agentName is provided, use it directly
                if (!string.IsNullOrEmpty(agentName))
                {
                    // Verify agent exists and belongs to tenant
                    var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
                    if (agent == null)
                    {
                        return Results.Json(new { error = "NotFound", message = $"Agent '{agentName}' not found in tenant '{tenantId}'" }, statusCode: 404);
                    }

                    // Check permissions
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to view this agent's knowledge" },
                            statusCode: 403);
                    }

                    agentNames = new List<string> { agentName };
                }
                // If activationName is provided, resolve to agent names
                else if (!string.IsNullOrEmpty(activationName))
                {
                    // TODO: Implement activation name resolution logic
                    // For now, return an error indicating this needs implementation
                    return Results.Json(
                        new { error = "NotImplemented", message = "Filtering by activationName is not yet implemented" },
                        statusCode: 501);
                }
                // If neither is provided, get all knowledge for the tenant (requires TenantAdmin or SysAdmin)
                else
                {
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: must specify agentName or have TenantAdmin/SysAdmin role" },
                            statusCode: 403);
                    }
                }

                // Get knowledge based on filters using service layer
                var knowledge = await knowledgeService.GetAllForTenantAsync(tenantId, agentNames);
                
                return Results.Ok(new { tenantId, agentName, activationName, knowledge });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("List Knowledge")
        .WithOpenApi(operation => {
            operation.Summary = "List knowledge for a tenant";
            operation.Description = "Retrieves knowledge items for a tenant. Can be filtered by agentName or activationName query parameters.";
            return operation;
        });

        // Get specific knowledge by ID
        adminKnowledgeGroup.MapGet("/{knowledgeId}", async (
            string tenantId,
            string knowledgeId,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get the knowledge using service layer
                var knowledge = await knowledgeService.GetByIdForTenantAsync(knowledgeId, tenantId);
                if (knowledge == null)
                {
                    return Results.Json(new { error = "NotFound", message = "Knowledge not found" }, statusCode: 404);
                }

                // If knowledge is agent-scoped, verify permissions
                if (!string.IsNullOrEmpty(knowledge.Agent))
                {
                    var agent = await agentRepository.GetByNameInternalAsync(knowledge.Agent, tenantId);
                    if (agent != null && !CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to view this knowledge" },
                            statusCode: 403);
                    }
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
            operation.Description = "Retrieves a specific knowledge item by its ID.";
            return operation;
        });

        // Create knowledge
        adminKnowledgeGroup.MapPost("", async (
            string tenantId,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromBody] CreateKnowledgeRequest request,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                string? resolvedAgentName = null;

                // If agentName is provided, use it
                if (!string.IsNullOrEmpty(agentName))
                {
                    // Verify agent exists and belongs to tenant
                    var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
                    if (agent == null)
                    {
                        return Results.Json(new { error = "NotFound", message = $"Agent '{agentName}' not found in tenant '{tenantId}'" }, statusCode: 404);
                    }

                    // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this agent's knowledge" },
                            statusCode: 403);
                    }

                    resolvedAgentName = agentName;
                }
                // If activationName is provided, resolve to agent name
                else if (!string.IsNullOrEmpty(activationName))
                {
                    // TODO: Implement activation name resolution logic
                    return Results.Json(
                        new { error = "NotImplemented", message = "Creating knowledge by activationName is not yet implemented" },
                        statusCode: 501);
                }
                // If neither is provided, check if user has permissions to create tenant-level knowledge
                else
                {
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: must specify agentName or have TenantAdmin/SysAdmin role" },
                            statusCode: 403);
                    }
                }

                // Use service to create knowledge
                var knowledge = await knowledgeService.CreateForTenantAsync(
                    request.Name,
                    request.Content,
                    request.Type,
                    tenantId,
                    tenantContext.LoggedInUser,
                    resolvedAgentName,
                    request.Version
                );

                return Results.Created(AdminApiConstants.BuildVersionedPath($"tenants/{tenantId}/knowledge/{knowledge.Id}"), knowledge);
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
            operation.Summary = "Create knowledge";
            operation.Description = "Creates a new knowledge item. Can be scoped to an agent using agentName query parameter. If a knowledge item with the same content hash already exists, returns the existing item.";
            return operation;
        });

        // Update knowledge
        adminKnowledgeGroup.MapPatch("/{knowledgeId}", async (
            string tenantId,
            string knowledgeId,
            [FromBody] UpdateKnowledgeRequest request,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get existing knowledge using service layer
                var existingKnowledge = await knowledgeService.GetByIdForTenantAsync(knowledgeId, tenantId);
                if (existingKnowledge == null)
                {
                    return Results.Json(
                        new { error = "NotFound", message = "Knowledge not found" },
                        statusCode: 404);
                }

                // If knowledge is agent-scoped, check permissions
                if (!string.IsNullOrEmpty(existingKnowledge.Agent))
                {
                    var agent = await agentRepository.GetByNameInternalAsync(existingKnowledge.Agent, tenantId);
                    if (agent == null)
                    {
                        return Results.Json(
                            new { error = "NotFound", message = $"Agent '{existingKnowledge.Agent}' not found" },
                            statusCode: 404);
                    }

                    // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this knowledge" },
                            statusCode: 403);
                    }
                }
                else
                {
                    // Tenant-level knowledge requires TenantAdmin or SysAdmin
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to modify tenant-level knowledge" },
                            statusCode: 403);
                    }
                }

                // Update knowledge using service layer (creates new version)
                var updatedKnowledge = await knowledgeService.UpdateForTenantAsync(
                    knowledgeId,
                    request.Content,
                    request.Type,
                    tenantId,
                    tenantContext.LoggedInUser,
                    request.Version
                );
                
                return Results.Ok(updatedKnowledge);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new { error = "BadRequest", message = ex.Message },
                    statusCode: 400);
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
            string knowledgeId,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get the knowledge to verify tenant and permissions
                var knowledge = await knowledgeService.GetByIdForTenantAsync(knowledgeId, tenantId);
                if (knowledge == null)
                {
                    return Results.Json(new { error = "NotFound", message = "Knowledge not found" }, statusCode: 404);
                }

                // If knowledge is agent-scoped, check permissions
                if (!string.IsNullOrEmpty(knowledge.Agent))
                {
                    var agent = await agentRepository.GetByNameInternalAsync(knowledge.Agent, tenantId);
                    if (agent != null && !CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to delete this knowledge" },
                            statusCode: 403);
                    }
                }
                else
                {
                    // Tenant-level knowledge requires TenantAdmin or SysAdmin
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to delete tenant-level knowledge" },
                            statusCode: 403);
                    }
                }

                // Use service to delete knowledge
                var result = await knowledgeService.DeleteByIdForTenantAsync(knowledgeId, tenantId);
                if (!result)
                {
                    return Results.Json(new { error = "NotFound", message = "Knowledge not found or could not be deleted" }, statusCode: 404);
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
            string name,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                string? resolvedAgentName = null;

                // If agentName is provided, use it
                if (!string.IsNullOrEmpty(agentName))
                {
                    // Verify agent exists and belongs to tenant
                    var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
                    if (agent == null)
                    {
                        return Results.Json(new { error = "NotFound", message = $"Agent '{agentName}' not found in tenant '{tenantId}'" }, statusCode: 404);
                    }

                    // Check permissions
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to view this agent's knowledge" },
                            statusCode: 403);
                    }

                    resolvedAgentName = agentName;
                }
                // If activationName is provided, resolve to agent name
                else if (!string.IsNullOrEmpty(activationName))
                {
                    // TODO: Implement activation name resolution logic
                    return Results.Json(
                        new { error = "NotImplemented", message = "Filtering by activationName is not yet implemented" },
                        statusCode: 501);
                }
                // If neither is provided, require TenantAdmin or SysAdmin
                else
                {
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: must specify agentName or have TenantAdmin/SysAdmin role" },
                            statusCode: 403);
                    }
                }

                // Use service to get versions
                var versions = await knowledgeService.GetVersionsForTenantAsync(name, tenantId, resolvedAgentName);
                return Results.Ok(new { tenantId, name, agentName, activationName, versions });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Fetch Knowledge Versions")
        .WithOpenApi(operation => {
            operation.Summary = "Get all versions of knowledge by name";
            operation.Description = "Retrieves all versions of a knowledge item with a specific name. Can be filtered by agentName or activationName query parameters.";
            return operation;
        });

        // Delete all versions of knowledge by name
        adminKnowledgeGroup.MapDelete("/{name}/versions", async (
            string tenantId,
            string name,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                string? resolvedAgentName = null;

                // If agentName is provided, use it
                if (!string.IsNullOrEmpty(agentName))
                {
                    // Verify agent exists and belongs to tenant
                    var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
                    if (agent == null)
                    {
                        return Results.Json(new { error = "NotFound", message = $"Agent '{agentName}' not found in tenant '{tenantId}'" }, statusCode: 404);
                    }

                    // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this agent's knowledge" },
                            statusCode: 403);
                    }

                    resolvedAgentName = agentName;
                }
                // If activationName is provided, resolve to agent name
                else if (!string.IsNullOrEmpty(activationName))
                {
                    // TODO: Implement activation name resolution logic
                    return Results.Json(
                        new { error = "NotImplemented", message = "Deleting by activationName is not yet implemented" },
                        statusCode: 501);
                }
                // If neither is provided, require TenantAdmin or SysAdmin
                else
                {
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: must specify agentName or have TenantAdmin/SysAdmin role" },
                            statusCode: 403);
                    }
                }

                // Use service to delete all versions
                var result = await knowledgeService.DeleteAllVersionsForTenantAsync(name, tenantId, resolvedAgentName);
                if (!result)
                {
                    return Results.Json(new { error = "NotFound", message = "Knowledge not found or could not be deleted" }, statusCode: 404);
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
            operation.Description = "Deletes all versions of a knowledge item with a specific name. Can be filtered by agentName or activationName query parameters.";
            return operation;
        });
    }
}


