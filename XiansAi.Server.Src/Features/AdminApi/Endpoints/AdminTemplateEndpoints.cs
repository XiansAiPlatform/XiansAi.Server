using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using Shared.Auth;
using Features.AdminApi.Auth;

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
        
        public List<string>? SamplePrompts { get; set; }
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
        ;

        // Get Agent Template Details by ObjectId
        adminTemplateGroup.MapGet("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromServices] ITemplateService templateService) =>
        {
            var result = await templateService.GetSystemScopedAgentByIdAsync(templateObjectId);
            return result.ToHttpResult();
        })
        .WithName("GetAgentTemplateDetails")
        ;

        // Update Agent Template
        adminTemplateGroup.MapPatch("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromBody] UpdateAgentTemplateRequest request,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Templates are system-scoped (global) resources; only SysAdmins may modify them.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext))
            {
                return Results.Json(
                    new { message = "Access denied: Only system administrators can modify agent templates" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await templateService.UpdateSystemScopedAgentAsync(
                templateObjectId,
                request.Description,
                request.OnboardingJson,
                request.OwnerAccess,
                request.ReadAccess,
                request.WriteAccess,
                request.SamplePrompts);
            return result.ToHttpResult();
        })
        .WithName("UpdateAgentTemplate")
        ;

        // Delete Agent Template
        adminTemplateGroup.MapDelete("/agentTemplates/{templateObjectId}", async (
            string templateObjectId,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Templates are system-scoped (global) resources; only SysAdmins may delete them.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext))
            {
                return Results.Json(
                    new { message = "Access denied: Only system administrators can delete agent templates" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

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
        ;

        // List tenants that have a deployed instance of a template
        adminTemplateGroup.MapGet("/agentTemplates/{templateObjectId}/deployments", async (
            string templateObjectId,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Deployments span all tenants, so this cross-tenant view is restricted to SysAdmins.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext))
            {
                return Results.Json(
                    new { message = "Access denied: Only system administrators can view template deployments across tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await templateService.GetTemplateDeploymentsAsync(templateObjectId);
            return result.ToHttpResult();
        })
        .WithName("GetAgentTemplateDeployments")
        ;

        // Deploy Template to Tenant
        adminTemplateGroup.MapPost("/agentTemplates/{templateObjectId}/deploy", async (
            string templateObjectId,
            [FromQuery] string tenantId,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // SysAdmins may deploy a template into any tenant; other admins (e.g. TenantAdmin) may
            // only deploy into their own resolved tenant. This prevents cross-tenant deployment.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext) &&
                !string.Equals(tenantId, tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { message = "Access denied: you can only deploy templates to your own tenant" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // Get template to find its name
            var templateResult = await templateService.GetSystemScopedAgentByIdAsync(templateObjectId);
            if (!templateResult.IsSuccess)
            {
                return templateResult.ToHttpResult();
            }

            var template = templateResult.Data!;
            var agentName = template.Name;
            var result = await templateService.DeployTemplateToTenant(agentName, tenantId, tenantContext.LoggedInUser);
            return result.ToHttpResult();
        })
        .WithName("DeployTemplateToTenant")
        ;

        MapAdminTemplateByNameEndpoints(adminTemplateGroup);
    }

    /// <summary>
    /// Maps the name-based variants of the template endpoints. Templates are uniquely identified by
    /// their agent name, so these endpoints accept the template name under the `/by-name/` segment
    /// instead of a MongoDB ObjectId. Behaviour mirrors the ObjectId-based endpoints.
    /// </summary>
    private static void MapAdminTemplateByNameEndpoints(RouteGroupBuilder adminTemplateGroup)
    {
        // Get Agent Template Details by Name
        adminTemplateGroup.MapGet("/agentTemplates/by-name/{templateAgentName}", async (
            string templateAgentName,
            [FromServices] ITemplateService templateService) =>
        {
            var result = await templateService.GetSystemScopedAgentByNameAsync(templateAgentName);
            return result.ToHttpResult();
        })
        .WithName("GetAgentTemplateDetailsByName")
        ;

        // Update Agent Template by Name
        adminTemplateGroup.MapPatch("/agentTemplates/by-name/{templateAgentName}", async (
            string templateAgentName,
            [FromBody] UpdateAgentTemplateRequest request,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Templates are system-scoped (global) resources; only SysAdmins may modify them.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext))
            {
                return Results.Json(
                    new { message = "Access denied: Only system administrators can modify agent templates" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await templateService.UpdateSystemScopedAgentByNameAsync(
                templateAgentName,
                request.Description,
                request.OnboardingJson,
                request.OwnerAccess,
                request.ReadAccess,
                request.WriteAccess,
                request.SamplePrompts);
            return result.ToHttpResult();
        })
        .WithName("UpdateAgentTemplateByName")
        ;

        // Delete Agent Template by Name
        adminTemplateGroup.MapDelete("/agentTemplates/by-name/{templateAgentName}", async (
            string templateAgentName,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Templates are system-scoped (global) resources; only SysAdmins may delete them.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext))
            {
                return Results.Json(
                    new { message = "Access denied: Only system administrators can delete agent templates" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // Resolve the template first so a non-existent / non-system-scoped name yields the right status.
            var templateResult = await templateService.GetSystemScopedAgentByNameAsync(templateAgentName);
            if (!templateResult.IsSuccess)
            {
                return templateResult.ToHttpResult();
            }

            var result = await templateService.DeleteSystemScopedAgent(templateResult.Data!.Name);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            return Results.NoContent();
        })
        .WithName("DeleteAgentTemplateByName")
        ;

        // List tenants that have a deployed instance of a template, by Name
        adminTemplateGroup.MapGet("/agentTemplates/by-name/{templateAgentName}/deployments", async (
            string templateAgentName,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Deployments span all tenants, so this cross-tenant view is restricted to SysAdmins.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext))
            {
                return Results.Json(
                    new { message = "Access denied: Only system administrators can view template deployments across tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await templateService.GetTemplateDeploymentsByNameAsync(templateAgentName);
            return result.ToHttpResult();
        })
        .WithName("GetAgentTemplateDeploymentsByName")
        ;

        // Deploy Template to Tenant by Name
        adminTemplateGroup.MapPost("/agentTemplates/by-name/{templateAgentName}/deploy", async (
            string templateAgentName,
            [FromQuery] string tenantId,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // SysAdmins may deploy a template into any tenant; other admins (e.g. TenantAdmin) may
            // only deploy into their own resolved tenant. This prevents cross-tenant deployment.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext) &&
                !string.Equals(tenantId, tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { message = "Access denied: you can only deploy templates to your own tenant" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await templateService.DeployTemplateToTenant(templateAgentName, tenantId, tenantContext.LoggedInUser);
            return result.ToHttpResult();
        })
        .WithName("DeployTemplateToTenantByName")
        ;
    }
}


