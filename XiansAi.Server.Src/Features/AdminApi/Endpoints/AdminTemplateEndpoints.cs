using Features.AdminApi.Auth;
using Features.AdminApi.Utils;
using Features.WebApi.Auth;
using Shared.Services;
using Shared.Auth;
using Shared.Repositories;
using Shared.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for agent template management.
/// These are administrative operations for browsing and managing agent templates.
/// </summary>
public static class AdminTemplateEndpoints
{
    /// <summary>
    /// Request model for creating an agent template.
    /// </summary>
    public class CreateAgentTemplateRequest
    {
        [Required]
        public required string Name { get; set; }
        
        public string? Description { get; set; }
        
        public string? Category { get; set; }
        
        public string? Icon { get; set; }
        
        public List<SamplePrompt>? SamplePrompts { get; set; }
        
        public string? OnboardingJson { get; set; }
        
        public List<string>? OwnerAccess { get; set; }
        
        public List<string>? ReadAccess { get; set; }
        
        public List<string>? WriteAccess { get; set; }
    }

    /// <summary>
    /// Request model for updating an agent template.
    /// </summary>
    public class UpdateAgentTemplateRequest
    {
        public string? Description { get; set; }
        
        public string? Category { get; set; }
        
        public string? Icon { get; set; }
        
        public List<SamplePrompt>? SamplePrompts { get; set; }
        
        public string? OnboardingJson { get; set; }
        
        public List<string>? OwnerAccess { get; set; }
        
        public List<string>? ReadAccess { get; set; }
        
        public List<string>? WriteAccess { get; set; }
    }

    /// <summary>
    /// Maps all AdminApi template endpoints.
    /// </summary>
    public static void MapAdminTemplateEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        // List Agent Templates (plural) - System Admin only, no X-Tenant-Id required
        adminApiGroup.MapGet("/agentTemplates", async (
            [FromQuery] bool? basicDataOnly,
            HttpContext httpContext,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ILogger<Program> logger) =>
        {
            // Check SysAdmin - no X-Tenant-Id required
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(
                tenantContext, userRepository, httpContext, logger);
            if (!isSysAdmin)
            {
                return errorResult ?? Results.Json(
                    new { error = "Forbidden", message = "Access denied: System admin permissions required" },
                    statusCode: 403);
            }

            // Reuse existing template service
            var result = await templateService.GetSystemScopedAgentDefinitions(basicDataOnly ?? false);
            return result.ToHttpResult();
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("BrowseAgentTemplates")
        .RequiresToken()
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Browse Agent Templates (System Admin)",
            Description = "Browse all available agent templates. System admin only. No X-Tenant-Id header required."
        });

        // List Agent Templates for Tenant - Tenant-scoped, X-Tenant-Id required
        adminApiGroup.MapGet("/tenants/{tenantId}/agentTemplates", async (
            string tenantId,
            [FromQuery] bool? basicDataOnly,
            [FromServices] ITemplateService templateService) =>
        {
            // Reuse existing template service
            // Future: Add eligibility filtering based on tenant
            var result = await templateService.GetSystemScopedAgentDefinitions(basicDataOnly ?? false);
            return result.ToHttpResult();
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("BrowseAgentTemplatesForTenant")
        .RequiresAdminApiAuth()
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Browse Agent Templates for Tenant",
            Description = "Browse agent templates eligible for a specific tenant. X-Tenant-Id header required. Future: Eligibility filtering will be implemented."
        });

        // Get Agent Template Details (singular) - System Admin only, no X-Tenant-Id required
        adminApiGroup.MapGet("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromQuery] bool? basicDataOnly,
            HttpContext httpContext,
            [FromServices] IAgentTemplateRepository templateRepository,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ILogger<Program> logger) =>
        {
            // Check SysAdmin - no X-Tenant-Id required
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(
                tenantContext, userRepository, httpContext, logger);
            if (!isSysAdmin)
            {
                return errorResult ?? Results.Json(
                    new { error = "Forbidden", message = "Access denied: System admin permissions required" },
                    statusCode: 403);
            }

            // Get template by MongoDB ObjectId
            var template = await templateRepository.GetByIdAsync(templateObjectId);
            if (template == null)
            {
                return Results.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            // If basicDataOnly is false, we might want to include more details
            // For now, return the template as-is
            return Results.Ok(template);
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("GetAgentTemplateDetails")
        .RequiresToken()
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Agent Template Details (System Admin)",
            Description = "Get detailed information about an agent template. System admin only. No X-Tenant-Id header required."
        });

        // Update Agent Template Metadata - System Admin only, no X-Tenant-Id required
        // Note: Templates are created when deploying agents, not via a separate create endpoint
        adminApiGroup.MapPatch("/agentTemplates/{templateObjectId}/Metadata", async (
            string templateObjectId,
            [FromBody] UpdateAgentTemplateRequest request,
            HttpContext httpContext,
            [FromServices] IAgentTemplateRepository templateRepository,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ILogger<Program> logger) =>
        {
            // Check SysAdmin - no X-Tenant-Id required
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(
                tenantContext, userRepository, httpContext, logger);
            if (!isSysAdmin)
            {
                return errorResult ?? Results.Json(
                    new { error = "Forbidden", message = "Access denied: System admin permissions required" },
                    statusCode: 403);
            }

            // Get existing template by MongoDB ObjectId
            var existingTemplate = await templateRepository.GetByIdAsync(templateObjectId);
            if (existingTemplate == null)
            {
                return Results.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            // Only update metadata fields - don't modify core template properties
            if (request.Description != null)
            {
                existingTemplate.SetDescription(request.Description);
            }
            if (request.Category != null)
            {
                existingTemplate.SetCategory(request.Category);
            }
            if (request.Icon != null)
            {
                existingTemplate.SetIcon(request.Icon);
            }
            if (request.SamplePrompts != null)
            {
                existingTemplate.SetSamplePrompts(request.SamplePrompts);
            }

            // Update access lists if provided
            if (request.OwnerAccess != null)
            {
                existingTemplate.OwnerAccess = request.OwnerAccess;
            }
            if (request.ReadAccess != null)
            {
                existingTemplate.ReadAccess = request.ReadAccess;
            }
            if (request.WriteAccess != null)
            {
                existingTemplate.WriteAccess = request.WriteAccess;
            }

            // Update onboarding JSON if provided
            if (request.OnboardingJson != null)
            {
                existingTemplate.OnboardingJson = request.OnboardingJson;
            }

            // Update in repository - only metadata and related fields are updated
            // Don't call SanitizeAndValidate() as it might fail on existing data
            var success = await templateRepository.UpdateAsync(existingTemplate.Id, existingTemplate);
            if (!success)
            {
                return Results.Problem("Failed to update agent template metadata");
            }

            return Results.Ok(existingTemplate);
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("UpdateAgentTemplateMetadata")
        .RequiresToken()
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Agent Template Metadata (System Admin)",
            Description = "Update metadata of an existing agent template (category, description, icon, samplePrompts). System admin only. No X-Tenant-Id header required."
        });

        // Delete Agent Template - System Admin only, no X-Tenant-Id required
        adminApiGroup.MapDelete("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            HttpContext httpContext,
            [FromServices] IAgentTemplateRepository templateRepository,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ILogger<Program> logger) =>
        {
            // Check SysAdmin - no X-Tenant-Id required
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(
                tenantContext, userRepository, httpContext, logger);
            if (!isSysAdmin)
            {
                return errorResult ?? Results.Json(
                    new { error = "Forbidden", message = "Access denied: System admin permissions required" },
                    statusCode: 403);
            }

            // Get existing template by MongoDB ObjectId
            var existingTemplate = await templateRepository.GetByIdAsync(templateObjectId);
            if (existingTemplate == null)
            {
                return Results.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            // Delete from repository
            var success = await templateRepository.DeleteAsync(existingTemplate.Id);
            if (!success)
            {
                return Results.Problem("Failed to delete agent template");
            }

            return Results.NoContent();
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("DeleteAgentTemplate")
        .RequiresToken()
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Agent Template (System Admin)",
            Description = "Delete an agent template. System admin only. No X-Tenant-Id header required."
        });

        // Deploy Template to Tenant - X-Tenant-Id required
        // This endpoint handles both:
        // 1. New templates from agent_templates collection (by ObjectId)
        // 2. Legacy system-scoped templates from agents collection (by ObjectId, SystemScoped = true)
        // Future: Eligibility validation will be implemented here
        adminApiGroup.MapPost("/agentTemplates/{templateObjectId}/deploy", async (
            string templateObjectId,
            [FromQuery] string tenantId,
            [FromServices] ITemplateService templateService,
            [FromServices] IAgentTemplateRepository templateRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            // X-Tenant-Id is required (enforced by RequiresAdminApiAuth)
            // Future: Add eligibility validation here
            
            string? agentName = null;
            
            // First, try to find in agent_templates collection (new templates)
            var template = await templateRepository.GetByIdAsync(templateObjectId);
            if (template != null)
            {
                agentName = template.Name;
            }
            else
            {
                // If not found, try agents collection (legacy system-scoped templates)
                var legacyAgent = await agentRepository.GetByIdInternalAsync(templateObjectId);
                if (legacyAgent != null && legacyAgent.SystemScoped)
                {
                    agentName = legacyAgent.Name;
                }
            }
            
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.NotFound($"Agent template with ID '{templateObjectId}' not found in either agent_templates or agents collection");
            }

            // Deploy template to specific tenant (AdminApi allows deploying to any tenant)
            // Use DeployTemplateToTenant method which accepts explicit tenant/user parameters
            // This method handles both new templates and legacy agents
            var result = await templateService.DeployTemplateToTenant(agentName, tenantId, tenantContext.LoggedInUser, null);
            return result.ToHttpResult();
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("DeployTemplateToTenant")
        .RequiresAdminApiAuth()
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Deploy Agent Template to Tenant",
            Description = "Deploy an agent template to a specific tenant. X-Tenant-Id header required. Future: Eligibility validation will be implemented."
        });
    }
}

