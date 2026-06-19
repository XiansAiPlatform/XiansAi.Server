using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Shared.Utils.Services;
using Shared.Auth;
using Features.AdminApi.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for workflow management.
/// These are administrative operations for managing workflows.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class WorkflowManagementEndpoints
{
    /// <summary>
    /// Maps all AdminApi workflow management endpoints.
    /// </summary>
    public static void MapWorkflowManagementEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminWorkflowGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/workflows")
            .WithTags("AdminAPI - Workflow Management")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        // Activate Workflow (Start/Create New Workflow)
        adminWorkflowGroup.MapPost("/activate", async (
            string tenantId,
            [FromBody] WorkflowRequest request,
            [FromQuery] string? userId,
            [FromServices] IWorkflowStarterService workflowStarterService) =>
        {
            var result = await workflowStarterService.HandleStartWorkflow(request, userId);
            return result.ToHttpResult();
        })
        .WithName("ActivateWorkflow")
        ;

        // Get Workflow by ID
        adminWorkflowGroup.MapGet("", async Task<IResult> (
            string tenantId,
            [FromQuery] string workflowId,
            [FromQuery] string? runId,
            [FromServices] IWorkflowFinderService workflowFinderService,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (!AdminTenantScopeGuard.WorkflowIdBelongsToContext(tenantContext, workflowId))
            {
                return Results.NotFound(new { message = "Workflow not found in the specified tenant" });
            }

            var result = await workflowFinderService.GetWorkflow(workflowId, runId);
            return result.ToHttpResult();
        })
        .WithName("GetWorkflow")
        ;

        // List Workflows
        adminWorkflowGroup.MapGet("/list", async (
            string tenantId,
            [FromQuery] string? status,
            [FromQuery] string? agent,
            [FromQuery] string? workflowType,
            [FromQuery] string? user,
            [FromQuery] string? idPostfix,
            [FromQuery] int? pageSize,
            [FromQuery] string? pageToken,
            [FromServices] IWorkflowFinderService workflowFinderService) =>
        {
            var result = await workflowFinderService.GetWorkflows(status, agent, workflowType, user, idPostfix, pageSize, pageToken);
            return result.ToHttpResult();
        })
        .WithName("ListWorkflows")
        ;

        // Get Workflow Events
        adminWorkflowGroup.MapGet("/events", async Task<IResult> (
            string tenantId,
            [FromQuery] string workflowId,
            [FromServices] IWorkflowEventsService workflowEventsService,
            [FromServices] ITenantContext tenantContext) =>
        {
            // WorkflowEventsService applies no tenant scoping; enforce tenant ownership via the
            // tenant-prefixed workflow ID to prevent cross-tenant event/history disclosure.
            if (!AdminTenantScopeGuard.WorkflowIdBelongsToContext(tenantContext, workflowId))
            {
                return Results.NotFound(new { message = "Workflow not found in the specified tenant" });
            }

            var result = await workflowEventsService.GetWorkflowEvents(workflowId);
            return result.ToHttpResult();
        })
        .WithName("GetWorkflowEvents")
        ;

        // Stream Workflow Events
        adminWorkflowGroup.MapGet("/events/stream", (
            string tenantId,
            [FromQuery] string workflowId,
            [FromServices] IWorkflowEventsService workflowEventsService,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (!AdminTenantScopeGuard.WorkflowIdBelongsToContext(tenantContext, workflowId))
            {
                return Results.NotFound(new { message = "Workflow not found in the specified tenant" });
            }

            return workflowEventsService.StreamWorkflowEvents(workflowId);
        })
        .WithName("StreamWorkflowEvents")
        ;

        // Get Workflow Types
        adminWorkflowGroup.MapGet("/types", async (
            string tenantId,
            [FromQuery] string agent,
            [FromServices] IWorkflowFinderService workflowFinderService) =>
        {
            var result = await workflowFinderService.GetWorkflowTypes(agent);
            return result.ToHttpResult();
        })
        .WithName("GetWorkflowTypes")
        ;

        // Cancel Workflow
        adminWorkflowGroup.MapPost("/cancel", async Task<IResult> (
            string tenantId,
            [FromQuery] string workflowId,
            [FromQuery] bool force,
            [FromServices] IWorkflowCancelService workflowCancelService,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (!AdminTenantScopeGuard.WorkflowIdBelongsToContext(tenantContext, workflowId))
            {
                return Results.NotFound(new { message = "Workflow not found in the specified tenant" });
            }

            var result = await workflowCancelService.CancelWorkflow(workflowId, force);
            return result.ToHttpResult();
        })
        .WithName("CancelWorkflow")
        ;
    }
}

