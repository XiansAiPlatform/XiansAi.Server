using Shared.Services;
using Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Data.Models;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for agent activation management.
/// These endpoints allow creating, activating, deactivating, and deleting agent activations.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminAgentActivationEndpoints
{
    /// <summary>
    /// Maps all AdminApi agent activation endpoints.
    /// </summary>
    public static void MapAdminAgentActivationEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var activationGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agentActivations")
            .WithTags("AdminAPI - Agent Activation")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List all activations for a tenant
        activationGroup.MapGet("", async (
            string tenantId,
            [FromQuery] string? agentName,
            [FromServices] IActivationService activationService) =>
        {
            var result = await activationService.GetActivationsByTenantAsync(tenantId, agentName);
            return result.ToHttpResult();
        })
        .WithName("ListActivations")
        ;

        // Get activation by ID
        activationGroup.MapGet("/{activationId}", async (
            string tenantId,
            string activationId,
            [FromServices] IActivationService activationService) =>
        {
            var result = await activationService.GetActivationByIdAsync(activationId);
            return result.ToHttpResult();
        })
        .WithName("GetActivation")
        ;

        // Create a new activation
        activationGroup.MapPost("", async (
            string tenantId,
            [FromBody] CreateActivationRequest request,
            [FromServices] IActivationService activationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var userId = tenantContext.LoggedInUser ?? "system";
            var result = await activationService.CreateActivationAsync(request, userId, tenantId);
            return result.ToHttpResult();
        })
        .WithName("CreateActivation")
        ;

        // Update an existing activation
        activationGroup.MapPut("/{activationId}", async (
            string tenantId,
            string activationId,
            [FromBody] UpdateActivationRequest request,
            [FromServices] IActivationService activationService) =>
        {
            var result = await activationService.UpdateActivationAsync(activationId, request, tenantId);
            return result.ToHttpResult();
        })
        .WithName("UpdateActivation")
        ;

        // Activate an agent (start workflow)
        activationGroup.MapPost("/{activationId}/activate", async (
            string tenantId,
            string activationId,
            [FromBody] ActivateAgentRequest? request,
            [FromServices] IActivationService activationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            
            var result = await activationService.ActivateAgentAsync(activationId, tenantId, request?.WorkflowConfiguration);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new 
            { 
                message = $"Agent activation '{activationId}' activated successfully",
                workflowIds = result.Data?.WorkflowIds,
                workflowCount = result.Data?.WorkflowIds?.Count ?? 0,
                activation = result.Data
            });
        })
        .WithName("ActivateAgent")
        ;

        // Deactivate an agent (cancel workflow)
        activationGroup.MapPost("/{activationId}/deactivate", async (
            string tenantId,
            string activationId,
            [FromServices] IActivationService activationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var result = await activationService.DeactivateAgentAsync(activationId, tenantId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new 
            { 
                message = $"Agent activation '{activationId}' deactivated successfully",
                activation = result.Data
            });
        })
        .WithName("DeactivateAgent")
        ;

        // Delete an activation
        activationGroup.MapDelete("/{activationId}", async (
            string tenantId,
            string activationId,
            [FromServices] IActivationService activationService) =>
        {
            var result = await activationService.DeleteActivationAsync(activationId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = $"Activation '{activationId}' deleted successfully" });
        })
        .WithName("DeleteActivation")
        ;
    }
}

/// <summary>
/// Request model for activating an agent with optional workflow configuration
/// </summary>
public class ActivateAgentRequest
{
    public ActivationWorkflowConfiguration? WorkflowConfiguration { get; set; }
}
