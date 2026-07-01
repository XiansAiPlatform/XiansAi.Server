using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using Shared.Auth;
using Features.AdminApi.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for agent management.
/// These are administrative operations, separate from WebApi (UI) endpoints.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminAgentDeploymentEndpoints
{

    /// <summary>
    /// Maps all AdminApi agent management endpoints.
    /// </summary>
    public static void MapAdminAgentDeploymentsEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminAgentGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agentDeployments")
            .WithTags("AdminAPI - Agent Deployment")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        // List Agent deployments
        adminAgentGroup.MapGet("", async (
            string tenantId,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromServices] IAdminAgentService adminAgentService) =>
        {
            var result = await adminAgentService.GetAgentDeploymentsAsync(tenantId, page, pageSize);
            return result.ToHttpResult();
        })
        .WithName("ListAgentDeployments")
        ;

        // Get Agent Instance
        adminAgentGroup.MapGet("/{agentName}", async (
            string tenantId,
            string agentName,
            [FromServices] IAdminAgentService adminAgentService) =>
        {
            var result = await adminAgentService.GetAgentDeploymentByNameAsync(agentName, tenantId);
            return result.ToHttpResult();
        })
        .WithName("GetAgentDeployment")
        ;

        // Update Agent Instance
        adminAgentGroup.MapPatch("/{agentName}", async (
            string tenantId,
            string agentName,
            [FromBody] UpdateAgentRequest request,
            [FromServices] IAdminAgentService adminAgentService) =>
        {
            var result = await adminAgentService.UpdateAgentDeploymentAsync(agentName, tenantId, request);
            return result.ToHttpResult();
        })
        .WithName("UpdateAgentDeployment")
        ;

        // Delete Agent Instance
        adminAgentGroup.MapDelete("/{agentName}", async (
            string tenantId,
            string agentName,
            [FromServices] IAdminAgentService adminAgentService) =>
        {
            var result = await adminAgentService.DeleteAgentDeploymentAsync(agentName, tenantId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = $"Agent '{agentName}' deleted successfully" });
        })
        .WithName("DeleteAgentDeployment")
        ;

        // Promote a running tenant-scoped agent into a new system-scoped template
        adminAgentGroup.MapPost("/{agentName}/promote-to-template", async (
            string tenantId,
            string agentName,
            [FromServices] ITemplateService templateService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // Promoting creates a new system-scoped (global) template; only SysAdmins may do this.
            if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext))
            {
                return Results.Json(
                    new { message = "Access denied: Only system administrators can promote an agent to a template" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await templateService.PromoteAgentToTemplateAsync(agentName, tenantId, tenantContext.LoggedInUser);
            return result.ToHttpResult();
        })
        .WithName("PromoteAgentDeploymentToTemplate")
        ;
    }
}


