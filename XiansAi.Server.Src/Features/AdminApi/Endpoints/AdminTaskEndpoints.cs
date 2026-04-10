using Shared.Services;
using Shared.Utils.Services;
using Microsoft.AspNetCore.Mvc;
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
        .Produces<AdminPaginatedTasksResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("ListTasks")
        ;

        // Get task by workflow ID
        taskGroup.MapGet("/by-id", async Task<IResult> (
            string tenantId,
            [FromQuery] string taskId,
            [FromServices] IAdminTaskService taskService) =>
        {
            var result = await taskService.GetTaskById(taskId);
            
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
        ;

        // Update draft for a task
        taskGroup.MapPut("/draft", async (
            [FromQuery] string taskId,
            [FromBody] UpdateDraftRequest request,
            [FromServices] IAdminTaskService taskService) =>
        {
            var result = await taskService.UpdateDraft(taskId, request.UpdatedDraft);
            return result.ToHttpResult();
        })
        .Produces<object>()
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("UpdateTaskDraft")
        ;

        // Perform action on a task
        taskGroup.MapPost("/actions", async (
            [FromQuery] string taskId,
            [FromBody] PerformActionRequest request,
            [FromServices] IAdminTaskService taskService) =>
        {
            var result = await taskService.PerformAction(taskId, request.Action, request.Comment);
            return result.ToHttpResult();
        })
        .Produces<object>()
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("PerformTaskAction")
        ;
    }
}
