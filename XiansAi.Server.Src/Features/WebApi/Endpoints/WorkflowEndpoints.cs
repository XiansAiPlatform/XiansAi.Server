using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        // Map workflow endpoints with common attributes
        var workflowsGroup = app.MapGroup("/api/client/workflows")
            .WithTags("WebAPI - Workflows")
            .RequiresValidTenant()
            .RequireAuthorization();

        workflowsGroup.MapGet("/", async (
            [FromQuery] string workflowId,
            [FromQuery] string? runId,
            [FromServices] IWorkflowFinderService endpoint) =>
        {
            var result = await endpoint.GetWorkflow(workflowId, runId);
            return result.ToHttpResult();
        })
        .WithName("Get Workflow")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflow by ID";
            operation.Description = "Retrieves detailed information about a specific workflow by its workflow ID and optional run ID (passed as query parameters). Supports complex workflow IDs with special characters and embedded URLs.";
            return operation;
        });

        workflowsGroup.MapPost("/", async (
            [FromBody] WorkflowRequest request,
            [FromServices] IWorkflowStarterService endpoint) =>
        {
            var result = await endpoint.HandleStartWorkflow(request);
            return result.ToHttpResult();
        })
        .WithName("Create New Workflow")
        .WithOpenApi();

        workflowsGroup.MapGet("/events", async (
            [FromQuery] string workflowId,
            [FromServices] IWorkflowEventsService endpoint) =>
        {
            var result = await endpoint.GetWorkflowEvents(workflowId);
            return result.ToHttpResult();
        })
        .WithName("Get Workflow Events")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflow events";
            operation.Description = "Retrieves the event history for a specific workflow (pass workflowId as query parameter)";
            return operation;
        });

        workflowsGroup.MapGet("/list", async (
            [FromQuery] string? status,
            [FromQuery] string? agent,
            [FromQuery] string? workflowType,
            [FromQuery] string? user,
            [FromQuery] int? pageSize,
            [FromQuery] string? pageToken,
            [FromServices] IWorkflowFinderService endpoint) =>
        {
            var result = await endpoint.GetWorkflows(status, agent, workflowType, user, pageSize, pageToken);
            return result.ToHttpResult();
        })
        .WithName("Get Workflows")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflows with optional filters and pagination";
            operation.Description = "Retrieves workflows with optional filtering by status, agent, workflow type, and user, with pagination support";
            return operation;
        });

        workflowsGroup.MapGet("/types", async (
            [FromQuery] string agent,
            [FromServices] IWorkflowFinderService endpoint) =>
        {
            var result = await endpoint.GetWorkflowTypes(agent);
            return result.ToHttpResult();
        })
        .WithName("Get Workflow Types")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflow types for an agent";
            operation.Description = "Retrieves all unique workflow types for a specific agent";
            return operation;
        });

        workflowsGroup.MapPost("/cancel", async (
            [FromQuery] string workflowId,
            [FromQuery] bool force,
            [FromServices] IWorkflowCancelService endpoint) =>
        {
            var result = await endpoint.CancelWorkflow(workflowId, force);
            return result.ToHttpResult();
        })
        .WithName("Cancel Workflow")
        .WithOpenApi(operation => {
            operation.Summary = "Cancel a workflow";
            operation.Description = "Cancels a running workflow (pass workflowId and force as query parameters)";
            return operation;
        });

        workflowsGroup.MapGet("/events/stream", (
            [FromServices] IWorkflowEventsService endpoint,
            [FromQuery] string workflowId) =>
        {
            return endpoint.StreamWorkflowEvents(workflowId);
        })
        .WithName("Stream Workflow Events")
        .WithOpenApi(operation => {
            operation.Summary = "Stream workflow events in real-time";
            operation.Description = "Provides a server-sent events (SSE) stream of workflow activity events (pass workflowId as query parameter)";
            return operation;
        });
    }
} 