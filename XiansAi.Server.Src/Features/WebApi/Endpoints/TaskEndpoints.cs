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
        .WithOpenApi(operation => 
        {
            operation.Summary = "Get task by workflow ID";
            operation.Description = "Retrieves detailed information about a specific task by its workflow ID (pass as query parameter)";
            return operation;
        });

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
        .WithOpenApi(operation => 
        {
            operation.Summary = "Get paginated list of tasks";
            operation.Description = @"Retrieves a paginated list of tasks filtered by tenant. 
                Optional filters: agent (agent name), participantId (user ID), status (workflow execution status: Running, Completed, Failed, Terminated, Canceled, etc.). 
                Pagination: pageSize (default: 20, max: 100), pageToken (continuation token from previous response).";
            return operation;
        });

        taskGroup.MapPut("/draft", async (
            [FromQuery] string workflowId,
            [FromBody] UpdateDraftRequest request,
            [FromServices] ITaskService taskService) => 
        {
            var result = await taskService.UpdateDraft(workflowId, request.UpdatedDraft);
            
            return result.ToHttpResult();
        })
        .WithName("Update Task Draft")
        .WithOpenApi(operation => 
        {
            operation.Summary = "Update task draft";
            operation.Description = "Updates the draft work content for a task (pass workflowId as query parameter). This is typically used when a human is working on a task and wants to save their progress.";
            return operation;
        });

        taskGroup.MapPost("/complete", async (
            [FromQuery] string workflowId,
            [FromServices] ITaskService taskService) => 
        {
            var result = await taskService.CompleteTask(workflowId);
            return result.ToHttpResult();
        })
        .WithName("Complete Task")
        .WithOpenApi(operation => 
        {
            operation.Summary = "Complete a task";
            operation.Description = "Marks a task as completed (pass workflowId as query parameter). This signals the workflow that the human has finished their work and the workflow can continue.";
            return operation;
        });

        taskGroup.MapPost("/reject", async (
            [FromQuery] string workflowId,
            [FromBody] RejectTaskRequest request,
            [FromServices] ITaskService taskService) => 
        {
            var result = await taskService.RejectTask(workflowId, request.RejectionMessage);
            return result.ToHttpResult();
        })
        .WithName("Reject Task")
        .WithOpenApi(operation => 
        {
            operation.Summary = "Reject a task";
            operation.Description = "Rejects a task with a reason (pass workflowId as query parameter). This signals the workflow that the human cannot or will not complete the task.";
            return operation;
        });
    }
}

