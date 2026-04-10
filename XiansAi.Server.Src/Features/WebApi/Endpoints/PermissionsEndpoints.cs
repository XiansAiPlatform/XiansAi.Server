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
        
        .WithSummary("Get permissions for a specific agent")
        .WithDescription("Retrieves all permissions associated with the specified agent");

        permissionsGroup.MapPut("/agent/{agentName}", async (
            string agentName,
            [FromBody] PermissionDto permissions,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.UpdatePermissions(agentName, permissions);
            return result.ToHttpResult();
        })
        .WithName("Update Permissions")
        
        .WithSummary("Update permissions for a specific agent")
        .WithDescription("Updates all permissions associated with the specified agent");

        permissionsGroup.MapPost("/agent/{agentName}/users", async (
            string agentName,
            [FromBody] UserPermissionDto userPermission,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.AddUser(agentName, userPermission.UserId, userPermission.PermissionLevel);
            return result.ToHttpResult();
        })
        .WithName("Add User")
        
        .WithSummary("Add a user to an agent's permissions")
        .WithDescription("Adds a new user with specified permission level to an agent's permissions");

        permissionsGroup.MapDelete("/agent/{agentName}/users/{userId}", async (
            string agentName,
            string userId,
            [FromServices] IPermissionsService endpoint) =>
        {
            var result = await endpoint.RemoveUser(agentName, userId);
            return result.ToHttpResult();
        })
        .WithName("Remove User")
        
        .WithSummary("Remove a user from an agent's permissions")
        .WithDescription("Removes a user from an agent's permissions");

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
        
        .WithSummary("Update a user's permission level")
        .WithDescription("Updates the permission level of a user for a specific agent");

    }
}
