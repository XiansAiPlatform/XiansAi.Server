using Shared.Services;
using Shared.Repositories;
using Shared.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using Shared.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for agent template management.
/// Templates are agents with SystemScoped = true in the agents collection.
/// </summary>
public static class AdminTemplateEndpoints
{
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
        // List Agent Templates - System-scoped agents (SystemScoped = true)
        adminApiGroup.MapGet("/agentTemplates", async (
            [FromQuery] bool? basicDataOnly,
            [FromServices] ITemplateService templateService) =>
        {
            var result = await templateService.GetSystemScopedAgentDefinitions(basicDataOnly ?? false);
            return result.ToHttpResult();
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("BrowseAgentTemplates")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Browse Agent Templates",
            Description = "Browse all available agent templates (SystemScoped agents). No X-Tenant-Id header required."
        });

        // List Agent Templates for Tenant
        adminApiGroup.MapGet("/tenants/{tenantId}/agentTemplates", async (
            string tenantId,
            [FromQuery] bool? basicDataOnly,
            [FromServices] ITemplateService templateService) =>
        {
            var result = await templateService.GetSystemScopedAgentDefinitions(basicDataOnly ?? false);
            return result.ToHttpResult();
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("BrowseAgentTemplatesForTenant")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Browse Agent Templates for Tenant",
            Description = "Browse agent templates eligible for a specific tenant."
        });

        // Get Agent Template Details by ObjectId
        adminApiGroup.MapGet("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromServices] IAgentRepository agentRepository) =>
        {
            // Get template by MongoDB ObjectId (SystemScoped agent)
            var template = await agentRepository.GetByIdInternalAsync(templateObjectId);
            if (template == null || !template.SystemScoped)
            {
                return Results.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            return Results.Ok(template);
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("GetAgentTemplateDetails")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Agent Template Details",
            Description = "Get detailed information about an agent template. No X-Tenant-Id header required."
        });

        // Update Agent Template Metadata
        adminApiGroup.MapPatch("/agentTemplates/{templateObjectId}/Metadata", async (
            string templateObjectId,
            [FromBody] UpdateAgentTemplateRequest request,
            [FromServices] IAgentRepository agentRepository) =>
        {
            // Get existing template by MongoDB ObjectId
            var existingTemplate = await agentRepository.GetByIdInternalAsync(templateObjectId);
            if (existingTemplate == null || !existingTemplate.SystemScoped)
            {
                return Results.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            // Update metadata fields using Metadata dictionary
            var metadata = existingTemplate.Metadata ?? new Dictionary<string, object>();
            
            if (request.Description != null)
            {
                metadata["description"] = request.Description;
            }
            if (request.Category != null)
            {
                metadata["category"] = request.Category;
            }
            if (request.Icon != null)
            {
                metadata["icon"] = request.Icon;
            }
            if (request.SamplePrompts != null)
            {
                metadata["samplePrompts"] = request.SamplePrompts;
            }
            
            existingTemplate.Metadata = metadata;

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

            await agentRepository.UpdateInternalAsync(existingTemplate.Id, existingTemplate);

            return Results.Ok(existingTemplate);
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("UpdateAgentTemplateMetadata")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Agent Template Metadata",
            Description = "Update metadata of an existing agent template (category, description, icon, samplePrompts). No X-Tenant-Id header required."
        });

        // Delete Agent Template
        adminApiGroup.MapDelete("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromServices] IAgentRepository agentRepository) =>
        {
            // Get existing template by MongoDB ObjectId
            var existingTemplate = await agentRepository.GetByIdInternalAsync(templateObjectId);
            if (existingTemplate == null || !existingTemplate.SystemScoped)
            {
                return Results.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            var success = await agentRepository.DeleteAsync(existingTemplate.Id, "system", new string[] { });
            if (!success)
            {
                return Results.Problem("Failed to delete agent template");
            }

            return Results.NoContent();
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("DeleteAgentTemplate")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Agent Template",
            Description = "Delete an agent template. No X-Tenant-Id header required."
        });

        // Deploy Template to Tenant
        adminApiGroup.MapPost("/agentTemplates/{templateObjectId}/deploy", async (
            string templateObjectId,
            [FromQuery] string tenantId,
            [FromServices] ITemplateService templateService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Get template from agents collection (SystemScoped = true)
            var template = await agentRepository.GetByIdInternalAsync(templateObjectId);
            if (template == null || !template.SystemScoped)
            {
                return Results.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            var agentName = template.Name;
            var result = await templateService.DeployTemplateToTenant(agentName, tenantId, tenantContext.LoggedInUser ?? "system", null);
            return result.ToHttpResult();
        })
        .WithTags("AdminAPI - Agent Templates")
        .WithName("DeployTemplateToTenant")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Deploy Agent Template to Tenant",
            Description = "Deploy an agent template to a specific tenant."
        });
    }
}
