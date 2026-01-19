using Shared.Services;
using Shared.Repositories;
using Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Data.Models;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

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
            .RequireAuthorization("AdminEndpointAuthPolicy");

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List Agent Deployments",
            Description = "List all agent deployments for a tenant with optional pagination."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Agent Deployment",
            Description = "Get agent deployment details by agent name."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Agent Deployment",
            Description = "Update agent deployment (name, description, onboardingJson)."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Agent Deployment",
            Description = "Delete an agent instance."
        });
    }
}


