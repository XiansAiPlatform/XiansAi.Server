using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

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
            return await endpoint.GetWorkflow(workflowId, runId);
        })
        .WithName("Get Workflow")
        .WithOpenApi();

        workflowsGroup.MapPost("/", async (
            [FromBody] WorkflowRequest request,
            [FromServices] WorkflowStarterService endpoint) =>
        {
            return await endpoint.HandleStartWorkflow(request);
        })
        .WithName("Create New Workflow")
        .WithOpenApi();

        workflowsGroup.MapGet("/{workflowId}/events", async (
            string workflowId,
            [FromServices] WorkflowEventsService endpoint) =>
        {
            return await endpoint.GetWorkflowEvents(workflowId);
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
            return await endpoint.GetWorkflows(startTime, endTime, owner, status);
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
            [FromServices] WorkflowCancelService endpoint) =>
        {
            return await endpoint.CancelWorkflow(workflowId, force);
        })
        .WithName("Cancel Workflow")
        .WithOpenApi();

        workflowsGroup.MapGet("/{workflowId}/events/stream", (
            [FromServices] WorkflowEventsService endpoint,
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