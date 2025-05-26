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
            .RequiresValidTenant();

        workflowsGroup.MapGet("/{workflowId}/{runId}", async (
            string workflowId,
            string? runId,
            [FromServices] IWorkflowFinderService endpoint) =>
        {
            var result = await endpoint.GetWorkflow(workflowId, runId);
            return result.ToHttpResult();
        })
        .WithName("Get Workflow")
        .WithOpenApi();

        workflowsGroup.MapPost("/", async (
            [FromBody] WorkflowRequest request,
            [FromServices] IWorkflowStarterService endpoint) =>
        {
            var result = await endpoint.HandleStartWorkflow(request);
            return result.ToHttpResult();
        })
        .WithName("Create New Workflow")
        .WithOpenApi();

        workflowsGroup.MapGet("/{workflowId}/events", async (
            string workflowId,
            [FromServices] IWorkflowEventsService endpoint) =>
        {
            var result = await endpoint.GetWorkflowEvents(workflowId);
            return result.ToHttpResult();
        })
        .WithName("Get Workflow Events")
        .WithOpenApi();

        workflowsGroup.MapGet("/", async (
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] string? owner,
            [FromQuery] string? status,
            [FromServices] IWorkflowFinderService endpoint) =>
        {
            var result = await endpoint.GetWorkflows(startTime, endTime, owner, status);
            return result.ToHttpResult();
        })
        .WithName("Get Workflows")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflows with optional filters";
            operation.Description = "Retrieves workflows with optional filtering by date range and owner";
            return operation;
        });

        workflowsGroup.MapPost("/{workflowId}/cancel", async (
            string workflowId,
            [FromQuery] bool force,
            [FromServices] IWorkflowCancelService endpoint) =>
        {
            var result = await endpoint.CancelWorkflow(workflowId, force);
            return result.ToHttpResult();
        })
        .WithName("Cancel Workflow")
        .WithOpenApi();

        workflowsGroup.MapGet("/{workflowId}/events/stream", (
            [FromServices] IWorkflowEventsService endpoint,
            string workflowId) =>
        {
            return endpoint.StreamWorkflowEvents(workflowId);
        })
        .WithName("Stream Workflow Events")
        .WithOpenApi(operation => {
            operation.Summary = "Stream workflow events in real-time";
            operation.Description = "Provides a server-sent events (SSE) stream of workflow activity events";
            return operation;
        });
    }
} 