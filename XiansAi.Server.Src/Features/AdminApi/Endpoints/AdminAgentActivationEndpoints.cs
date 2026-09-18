using Shared.Services;
using Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Data.Models;
using Shared.Utils.Services;
using Features.AdminApi.Auth;

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
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

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
        .RequireCapability(CapabilityActions.TenantAgentActivationsList);

        // Get activation by ID
        activationGroup.MapGet("/{activationId}", async (
            string tenantId,
            string activationId,
            [FromServices] IActivationService activationService) =>
        {
            var result = await activationService.GetActivationByIdAsync(activationId);

            // Ensure the activation belongs to the caller's tenant (route tenant is validated
            // against the resolved context by TenantRouteScopeFilter). GetActivationByIdAsync
            // performs no tenant scoping, so enforce it here to prevent cross-tenant reads.
            if (result.IsSuccess && result.Data?.TenantId != tenantId)
            {
                return Results.NotFound(new { message = "Activation not found in the specified tenant" });
            }

            return result.ToHttpResult();
        })
        .WithName("GetActivation")
        .RequireCapability(CapabilityActions.TenantAgentActivationsGet);

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
        .RequireCapability(CapabilityActions.TenantAgentActivationsCreate);

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
        .RequireCapability(CapabilityActions.TenantAgentActivationsUpdate);

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
        .RequireCapability(CapabilityActions.TenantAgentActivationsActivate);

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
        .RequireCapability(CapabilityActions.TenantAgentActivationsDeactivate);

        // Delete an activation
        activationGroup.MapDelete("/{activationId}", async (
            string tenantId,
            string activationId,
            [FromServices] IActivationService activationService) =>
        {
            // Verify the activation belongs to the caller's tenant before deleting.
            // DeleteActivationAsync performs no tenant scoping, so enforce it here to
            // prevent cross-tenant deletion (route tenant is validated by TenantRouteScopeFilter).
            var existing = await activationService.GetActivationByIdAsync(activationId);
            if (!existing.IsSuccess)
            {
                return existing.ToHttpResult();
            }
            if (existing.Data?.TenantId != tenantId)
            {
                return Results.NotFound(new { message = "Activation not found in the specified tenant" });
            }

            var result = await activationService.DeleteActivationAsync(activationId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = $"Activation '{activationId}' deleted successfully" });
        })
        .WithName("DeleteActivation")
        .RequireCapability(CapabilityActions.TenantAgentActivationsDelete);
    }
}
