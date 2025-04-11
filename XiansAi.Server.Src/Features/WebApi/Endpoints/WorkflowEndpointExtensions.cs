using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class WorkflowEndpointExtensions
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/workflows/{workflowId}/{runId}", async (
            string workflowId,
            string? runId,
            [FromServices] WorkflowFinderService endpoint) =>
        {
            return await endpoint.GetWorkflow(workflowId, runId);
        })
        .WithName("Get Workflow")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapPost("/api/client/workflows", async (
            [FromBody] WorkflowRequest request,
            [FromServices] WorkflowStarterService endpoint) =>
        {
            return await endpoint.HandleStartWorkflow(request);
        })
        .WithName("Create New Workflow")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapGet("/api/client/workflows/{workflowId}/events", async (
            string workflowId,
            [FromServices] WorkflowEventsService endpoint) =>
        {
            return await endpoint.GetWorkflowEvents(workflowId);
        })
        .WithName("Get Workflow Events")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapGet("/api/client/workflows", async (
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] string? owner,
            [FromQuery] string? status,
            [FromServices] WorkflowFinderService endpoint) =>
        {
            return await endpoint.GetWorkflows(startTime, endTime, owner, status);
        })
        .WithName("Get Workflows")
        .RequiresValidTenant()
        .WithOpenApi(operation => {
            operation.Summary = "Get workflows with optional filters";
            operation.Description = "Retrieves workflows with optional filtering by date range and owner";
            return operation;
        });

        app.MapPost("/api/client/workflows/{workflowId}/cancel", async (
            string workflowId,
            [FromQuery] bool force,
            [FromServices] WorkflowCancelService endpoint) =>
        {
            return await endpoint.CancelWorkflow(workflowId, force);
        })
        .WithName("Cancel Workflow")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapGet("/api/client/workflows/{workflowId}/events/stream", (
            [FromServices] WorkflowEventsService endpoint,
            string workflowId) =>
        {
            return endpoint.StreamWorkflowEvents(workflowId);
        })
        .WithName("Stream Workflow Events")
        .RequiresValidTenant()
        .WithOpenApi(operation => {
            operation.Summary = "Stream workflow events in real-time";
            operation.Description = "Provides a server-sent events (SSE) stream of workflow activity events";
            return operation;
        });
    }
} 