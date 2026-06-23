using Shared.Services;
using Shared.Utils.Services;
using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Models;
using Features.AdminApi.Auth;

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
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

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
        taskGroup.MapPut("/draft", async Task<IResult> (
            string tenantId,
            [FromQuery] string taskId,
            [FromBody] UpdateDraftRequest request,
            [FromServices] IAdminTaskService taskService) =>
        {
            var ownershipError = await EnsureTaskInTenant(taskService, taskId, tenantId);
            if (ownershipError != null)
            {
                return ownershipError;
            }

            var result = await taskService.UpdateDraft(taskId, request.UpdatedDraft);
            return result.ToHttpResult();
        })
        .Produces<object>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("UpdateTaskDraft")
        ;

        // Perform action on a task
        taskGroup.MapPost("/actions", async Task<IResult> (
            string tenantId,
            [FromQuery] string taskId,
            [FromBody] PerformActionRequest request,
            [FromServices] IAdminTaskService taskService) =>
        {
            var ownershipError = await EnsureTaskInTenant(taskService, taskId, tenantId);
            if (ownershipError != null)
            {
                return ownershipError;
            }

            var result = await taskService.PerformAction(taskId, request.Action, request.Comment);
            return result.ToHttpResult();
        })
        .Produces<object>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("PerformTaskAction")
        ;
    }

    /// <summary>
    /// Verifies the task identified by <paramref name="taskId"/> belongs to <paramref name="tenantId"/>.
    /// The task service signals Temporal by workflow ID without any tenant scoping, so this check
    /// prevents cross-tenant task mutation. The route tenant is validated against the resolved
    /// context by <see cref="Auth.TenantRouteScopeFilter"/>.
    /// Returns null when the task is in the tenant; otherwise the error <see cref="IResult"/> to return.
    /// </summary>
    private static async Task<IResult?> EnsureTaskInTenant(
        IAdminTaskService taskService,
        string taskId,
        string tenantId)
    {
        var task = await taskService.GetTaskById(taskId);
        if (!task.IsSuccess)
        {
            return task.ToHttpResult();
        }
        if (task.Data?.TenantId != tenantId)
        {
            return Results.NotFound(new { message = "Task not found in the specified tenant" });
        }
        return null;
    }
}
