using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Features.WebApi.Services;
using Shared.Utils.Services;

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
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Activate Workflow (Start/Create New Workflow)
        adminWorkflowGroup.MapPost("/activate", async (
            string tenantId,
            [FromBody] WorkflowRequest request,
            [FromServices] IWorkflowStarterService workflowStarterService) =>
        {
            var result = await workflowStarterService.HandleStartWorkflow(request);
            return result.ToHttpResult();
        })
        .WithName("ActivateWorkflow")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Activate Workflow",
            Description = "Activate (start) a new workflow for a specific tenant. Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });

        // Get Workflow by ID
        adminWorkflowGroup.MapGet("", async (
            string tenantId,
            [FromQuery] string workflowId,
            [FromQuery] string? runId,
            [FromServices] IWorkflowFinderService workflowFinderService) =>
        {
            var result = await workflowFinderService.GetWorkflow(workflowId, runId);
            return result.ToHttpResult();
        })
        .WithName("GetWorkflow")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Workflow by ID",
            Description = "Retrieves detailed information about a specific workflow by its workflow ID and optional run ID (passed as query parameters). Supports complex workflow IDs with special characters and embedded URLs. Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });

        // List Workflows
        adminWorkflowGroup.MapGet("/list", async (
            string tenantId,
            [FromQuery] string? status,
            [FromQuery] string? agent,
            [FromQuery] string? workflowType,
            [FromQuery] string? user,
            [FromQuery] int? pageSize,
            [FromQuery] string? pageToken,
            [FromServices] IWorkflowFinderService workflowFinderService) =>
        {
            var result = await workflowFinderService.GetWorkflows(status, agent, workflowType, user, pageSize, pageToken);
            return result.ToHttpResult();
        })
        .WithName("ListWorkflows")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List Workflows",
            Description = "Retrieves workflows with optional filtering by status, agent, workflow type, and user, with pagination support. Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });

        // Get Workflow Events
        adminWorkflowGroup.MapGet("/events", async (
            string tenantId,
            [FromQuery] string workflowId,
            [FromServices] IWorkflowEventsService workflowEventsService) =>
        {
            var result = await workflowEventsService.GetWorkflowEvents(workflowId);
            return result.ToHttpResult();
        })
        .WithName("GetWorkflowEvents")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Workflow Events",
            Description = "Retrieves the event history for a specific workflow (pass workflowId as query parameter). Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });

        // Stream Workflow Events
        adminWorkflowGroup.MapGet("/events/stream", (
            string tenantId,
            [FromQuery] string workflowId,
            [FromServices] IWorkflowEventsService workflowEventsService) =>
        {
            return workflowEventsService.StreamWorkflowEvents(workflowId);
        })
        .WithName("StreamWorkflowEvents")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Stream Workflow Events",
            Description = "Provides a server-sent events (SSE) stream of workflow activity events (pass workflowId as query parameter). Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Workflow Types",
            Description = "Retrieves all unique workflow types for a specific agent. Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });

        // Cancel Workflow
        adminWorkflowGroup.MapPost("/cancel", async (
            string tenantId,
            [FromQuery] string workflowId,
            [FromQuery] bool force,
            [FromServices] IWorkflowCancelService workflowCancelService) =>
        {
            var result = await workflowCancelService.CancelWorkflow(workflowId, force);
            return result.ToHttpResult();
        })
        .WithName("CancelWorkflow")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Cancel Workflow",
            Description = "Cancels a running workflow (pass workflowId and force as query parameters). Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });
    }
}

