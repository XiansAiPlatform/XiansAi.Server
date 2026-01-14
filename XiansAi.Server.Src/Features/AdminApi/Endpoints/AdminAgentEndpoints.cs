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
public static class AdminAgentEndpoints
{

    /// <summary>
    /// Maps all AdminApi agent management endpoints.
    /// </summary>
    public static void MapAdminAgentEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminAgentGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agents")
            .WithTags("AdminAPI - Agent Management")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List Agent Instances
        adminAgentGroup.MapGet("", async (
            string tenantId,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromServices] IAdminAgentService adminAgentService) =>
        {
            var result = await adminAgentService.GetAgentInstancesAsync(tenantId, page, pageSize);
            return result.ToHttpResult();
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
            [FromServices] IAdminAgentService adminAgentService) =>
        {
            var result = await adminAgentService.GetAgentInstanceByIdAsync(agentId);
            return result.ToHttpResult();
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
            [FromServices] IAdminAgentService adminAgentService) =>
        {
            var result = await adminAgentService.UpdateAgentInstanceAsync(agentId, request);
            return result.ToHttpResult();
        })
        .WithName("UpdateAgentInstance")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Agent Instance",
            Description = "Update agent instance (name, description, onboardingJson)."
        });

        // Delete Agent Instance
        adminAgentGroup.MapDelete("/{agentId}", async (
            string tenantId,
            string agentId,
            [FromServices] IAdminAgentService adminAgentService) =>
        {
            var result = await adminAgentService.DeleteAgentInstanceAsync(agentId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = $"Agent '{agentId}' deleted successfully" });
        })
        .WithName("DeleteAgentInstance")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Agent Instance",
            Description = "Delete an agent instance."
        });
    }
}


