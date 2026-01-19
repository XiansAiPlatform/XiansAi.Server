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
        var adminTemplateGroup = adminApiGroup.MapGroup("")
            .WithTags("AdminAPI - Agent Templates")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List Agent Templates - System-scoped agents (SystemScoped = true)
        adminTemplateGroup.MapGet("/agentTemplates", async (
            [FromQuery] bool? basicDataOnly,
            [FromServices] ITemplateService templateService) =>
        {
            var result = await templateService.GetSystemScopedAgentDefinitions(basicDataOnly ?? false);
            return result.ToHttpResult();
        })
        .WithName("BrowseAgentTemplates")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Browse Agent Templates",
            Description = "Browse all available agent templates (SystemScoped agents). No X-Tenant-Id header required."
        });

        // Get Agent Template Details by ObjectId
        adminTemplateGroup.MapGet("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromServices] ITemplateService templateService) =>
        {
            var result = await templateService.GetSystemScopedAgentByIdAsync(templateObjectId);
            return result.ToHttpResult();
        })
        .WithName("GetAgentTemplateDetails")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Agent Template Details",
            Description = "Get detailed information about an agent template. No X-Tenant-Id header required."
        });

        // Update Agent Template
        adminTemplateGroup.MapPatch("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromBody] UpdateAgentTemplateRequest request,
            [FromServices] ITemplateService templateService) =>
        {
            var result = await templateService.UpdateSystemScopedAgentAsync(
                templateObjectId,
                request.Description,
                request.OnboardingJson,
                request.OwnerAccess,
                request.ReadAccess,
                request.WriteAccess);
            return result.ToHttpResult();
        })
        .WithName("UpdateAgentTemplate")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Agent Template",
            Description = "Update an existing agent template (description, onboardingJson, permissions). No X-Tenant-Id header required."
        });

        // Delete Agent Template
        adminTemplateGroup.MapDelete("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromServices] ITemplateService templateService) =>
        {
            // Get template to find its name
            var templateResult = await templateService.GetSystemScopedAgentByIdAsync(templateObjectId);
            if (!templateResult.IsSuccess)
            {
                return templateResult.ToHttpResult();
            }

            var template = templateResult.Data!;
            var result = await templateService.DeleteSystemScopedAgent(template.Name);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            return Results.NoContent();
        })
        .WithName("DeleteAgentTemplate")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Agent Template",
            Description = "Delete an agent template. No X-Tenant-Id header required."
        });

        // Deploy Template to Tenant
        adminTemplateGroup.MapPost("/agentTemplates/{templateObjectId}/deploy", async (
            string templateObjectId,
            [FromQuery] string tenantId,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Get template to find its name
            var templateResult = await templateService.GetSystemScopedAgentByIdAsync(templateObjectId);
            if (!templateResult.IsSuccess)
            {
                return templateResult.ToHttpResult();
            }

            var template = templateResult.Data!;
            var agentName = template.Name;
            var result = await templateService.DeployTemplateToTenant(agentName, tenantId, tenantContext.LoggedInUser ?? "system", null);
            return result.ToHttpResult();
        })
        .WithName("DeployTemplateToTenant")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Deploy Agent Template to Tenant",
            Description = "Deploy an agent template to a specific tenant."
        });
    }
}


