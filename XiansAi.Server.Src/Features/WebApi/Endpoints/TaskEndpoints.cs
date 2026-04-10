using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Shared.Utils.Services;
using Features.WebApi.Services;
using Features.WebApi.Models;

namespace Features.WebApi.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this WebApplication app)
    {
        // Map task endpoints with common attributes
        var taskGroup = app.MapGroup("/api/client/tasks")
            .WithTags("WebAPI - Tasks")
            .RequiresValidTenant()
            .RequireAuthorization();

        taskGroup.MapGet("/", async (
            [FromQuery] string workflowId,
            [FromServices] ITaskService taskService) => 
        {
            var result = await taskService.GetTaskById(workflowId);
            return result.ToHttpResult();
        })
        .WithName("Get Task by ID")
        
        .WithSummary("Get task by workflow ID")
        .WithDescription("Retrieves detailed information about a specific task by its workflow ID (pass as query parameter). Returns available actions for the task.");

        taskGroup.MapGet("/list", async (
            [FromServices] ITaskService taskService,
            [FromQuery] int? pageSize = null,
            [FromQuery] string? pageToken = null,
            [FromQuery] string? agent = null,
            [FromQuery] string? participantId = null,
            [FromQuery] string? status = null) => 
        {
            var result = await taskService.GetTasks(pageSize, pageToken, agent, participantId, status);
            return result.ToHttpResult();
        })
        .WithName("Get Tasks")
        
        .WithSummary("Get paginated list of tasks")
        .WithDescription(@"Retrieves a paginated list of tasks filtered by tenant. 
                Optional filters: agent (agent name), participantId (user ID), status (workflow execution status: Running, Completed, Failed, Terminated, Canceled, etc.). 
                Pagination: pageSize (default: 20, max: 100), pageToken (continuation token from previous response).
                Each task includes available actions from the workflow memo.");

        taskGroup.MapPut("/draft", async (
            [FromQuery] string workflowId,
            [FromBody] UpdateDraftRequest request,
            [FromServices] ITaskService taskService) => 
        {
            var result = await taskService.UpdateDraft(workflowId, request.UpdatedDraft);
            
            return result.ToHttpResult();
        })
        .WithName("Update Task Draft")
        
        .WithSummary("Update task draft")
        .WithDescription("Updates the draft work content for a task (pass workflowId as query parameter). This is typically used when a human is working on a task and wants to save their progress.");

        taskGroup.MapPost("/action", async (
            [FromQuery] string workflowId,
            [FromBody] PerformActionRequest request,
            [FromServices] ITaskService taskService) => 
        {
            var result = await taskService.PerformAction(workflowId, request.Action, request.Comment);
            return result.ToHttpResult();
        })
        .WithName("Perform Task Action")
        
        .WithSummary("Perform an action on a task")
        .WithDescription("Performs an action on a task (e.g., approve, reject, hold). The action should be one of the task's available actions. An optional comment can be provided.");
    }
}
