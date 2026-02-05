using Shared.Services;
using Shared.Utils.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Features.WebApi.Models;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for task management.
/// These endpoints allow viewing and managing tasks across tenants.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminTaskEndpoints
{
    /// <summary>
    /// Maps all AdminApi task endpoints.
    /// </summary>
    public static void MapAdminTaskEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var taskGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/tasks")
            .WithTags("AdminAPI - Tasks")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List all tasks for a tenant with optional filters
        taskGroup.MapGet("", async (
            string tenantId,
            [FromQuery] int? pageSize,
            [FromQuery] string? pageToken,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromQuery] string? participantId,
            [FromQuery] string? status,
            [FromServices] IAdminTaskService taskService) =>
        {
            // Normalize participantId to lowercase (typically an email)
            if (!string.IsNullOrEmpty(participantId))
            {
                participantId = participantId.ToLowerInvariant();
            }
            
            // Validate that activationName requires agentName
            if (!string.IsNullOrEmpty(activationName) && string.IsNullOrEmpty(agentName))
            {
                return Results.BadRequest(new { message = "activationName cannot be provided without agentName" });
            }
            
            var result = await taskService.GetTasks(
                tenantId,
                pageSize,
                pageToken,
                agentName,
                activationName,
                participantId,
                status);
            return result.ToHttpResult();
        })
        .Produces<AdminPaginatedTasksResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("ListTasks")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List Tasks",
            Description = @"List all tasks for a tenant with optional filtering.
            
Query Parameters:
- pageSize: Number of items per page (default: 20, max: 100)
- pageToken: Token for pagination (page number)
- agentName: Filter by agent name (exact match)
- activationName: Filter by activation name (maps to idPostfix search attribute)
- participantId: Filter by participant user ID
- status: Filter by execution status (e.g., Running, Completed, Failed)

Tasks are fetched from Temporal workflows with search attributes:
- tenantId: Tenant identifier
- agent: Agent name
- idPostfix: Activation identifier
- userId: Participant user ID

Returns a paginated list of tasks with metadata including available actions, status, and timestamps."
        });

        // Get task by workflow ID
        taskGroup.MapGet("/by-id", async Task<IResult> (
            string tenantId,
            [FromQuery] string workflowId,
            [FromServices] IAdminTaskService taskService) =>
        {
            var result = await taskService.GetTaskById(workflowId);
            
            // Verify the task belongs to the specified tenant
            if (result.IsSuccess && result.Data?.TenantId != tenantId)
            {
                return Results.NotFound(new { message = "Task not found in the specified tenant" });
            }
            
            return result.ToHttpResult();
        })
        .Produces<AdminTaskInfoResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetTask")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Task by ID",
            Description = @"Get detailed task information by workflow ID.
            
Returns complete task details including:
- Task metadata (title, description, participant)
- Workflow information (status, start/close times)
- Available actions and performed action (if completed)
- Admin-specific fields (agent name, activation name, tenant ID)
- Initial and final work content
- Custom metadata

The workflowId should be the full Temporal workflow ID."
        });

        // Update draft for a task
        taskGroup.MapPut("/{workflowId}/draft", async (
            string tenantId,
            string workflowId,
            [FromBody] UpdateDraftRequest request,
            [FromServices] IAdminTaskService taskService) =>
        {
            var result = await taskService.UpdateDraft(workflowId, request.UpdatedDraft);
            return result.ToHttpResult();
        })
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("UpdateTaskDraft")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Task Draft",
            Description = @"Update the draft work for a task.
            
This sends an 'UpdateDraft' signal to the Temporal workflow, allowing modification of the task's work in progress.

Request Body:
- updatedDraft: The new draft content (string)

Returns a success message if the draft was updated successfully."
        });

        // Perform action on a task
        taskGroup.MapPost("/{workflowId}/actions", async (
            string tenantId,
            string workflowId,
            [FromBody] PerformActionRequest request,
            [FromServices] IAdminTaskService taskService) =>
        {
            var result = await taskService.PerformAction(workflowId, request.Action, request.Comment);
            return result.ToHttpResult();
        })
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("PerformTaskAction")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Perform Action on Task",
            Description = @"Perform an action on a task with an optional comment.
            
This sends a 'PerformAction' signal to the Temporal workflow with the specified action and comment.
The action should be one of the available actions for the task (e.g., 'approve', 'reject', 'hold').

Request Body:
- action: The action to perform (required, should match one of the task's available actions)
- comment: Optional comment explaining the action

Returns a success message if the action was performed successfully."
        });
    }
}
