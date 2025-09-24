using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class PermissionsEndpoints
{
    public static void MapPermissionsEndpoints(this WebApplication app)
    {
        // Map permissions endpoints with common attributes
        var permissionsGroup = app.MapGroup("/api/client/permissions")
            .WithTags("WebAPI - Permissions")
            .RequiresValidTenant()
            .RequireAuthorization();

        permissionsGroup.MapGet("/agent/{agentName}", async (
            string agentName,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.GetPermissions(agentName);
            return result.ToHttpResult();
        })
        .WithName("Get Permissions")
        .WithOpenApi(operation => {
            operation.Summary = "Get permissions for a specific agent";
            operation.Description = "Retrieves all permissions associated with the specified agent";
            return operation;
        });

        permissionsGroup.MapPut("/agent/{agentName}", async (
            string agentName,
            [FromBody] PermissionDto permissions,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.UpdatePermissions(agentName, permissions);
            return result.ToHttpResult();
        })
        .WithName("Update Permissions")
        .WithOpenApi(operation => {
            operation.Summary = "Update permissions for a specific agent";
            operation.Description = "Updates all permissions associated with the specified agent";
            return operation;
        });

        permissionsGroup.MapPost("/agent/{agentName}/users", async (
            string agentName,
            [FromBody] UserPermissionDto userPermission,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.AddUser(agentName, userPermission.UserId, userPermission.PermissionLevel);
            return result.ToHttpResult();
        })
        .WithName("Add User")
        .WithOpenApi(operation => {
            operation.Summary = "Add a user to an agent's permissions";
            operation.Description = "Adds a new user with specified permission level to an agent's permissions";
            return operation;
        });

        permissionsGroup.MapDelete("/agent/{agentName}/users/{userId}", async (
            string agentName,
            string userId,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.RemoveUser(agentName, userId);
            return result.ToHttpResult();
        })
        .WithName("Remove User")
        .WithOpenApi(operation => {
            operation.Summary = "Remove a user from an agent's permissions";
            operation.Description = "Removes a user from an agent's permissions";
            return operation;
        });

        permissionsGroup.MapPatch("/agent/{agentName}/users/{userId}/{newPermissionLevel}", async (
            string agentName,
            string userId,
            string newPermissionLevel,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.UpdateUserPermission(agentName, userId, newPermissionLevel);
            return result.ToHttpResult();
        })
        .WithName("Update User Permission")
        .WithOpenApi(operation => {
            operation.Summary = "Update a user's permission level";
            operation.Description = "Updates the permission level of a user for a specific agent";
            return operation;
        });

        permissionsGroup.MapGet("/agent/{agentName}/check/read", async (
            string agentName,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.HasReadPermission(agentName);
            return result.ToHttpResult();
        })
        .WithName("Check Read Permission")
        .WithOpenApi(operation => {
            operation.Summary = "Check if current user has read permission";
            operation.Description = "Checks if the current user has read permission for the specified agent";
            return operation;
        });

        permissionsGroup.MapGet("/agent/{agentName}/check/write", async (
            string agentName,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.HasWritePermission(agentName);
            return result.ToHttpResult();
        })
        .WithName("Check Write Permission")
        .WithOpenApi(operation => {
            operation.Summary = "Check if current user has write permission";
            operation.Description = "Checks if the current user has write permission for the specified agent";
            return operation;
        });

        permissionsGroup.MapGet("/agent/{agentName}/check/owner", async (
            string agentName,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.HasOwnerPermission(agentName);
            return result.ToHttpResult();
        })
        .WithName("Check Owner Permission")
        .WithOpenApi(operation => {
            operation.Summary = "Check if current user has owner permission";
            operation.Description = "Checks if the current user has owner permission for the specified agent";
            return operation;
        });
    }
}
