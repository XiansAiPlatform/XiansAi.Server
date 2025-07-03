using Features.WebApi.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using System.Security.Claims;
using System.Text.Json.Serialization;
using XiansAi.Server.Features.WebApi.Services;

namespace XiansAi.Server.Features.WebApi.Endpoints;

public static class UserManagementEndpoints
{
    public static void MapUserManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("WebAPI - User Management")
            .RequireAuthorization();

        group.MapPost("/{userId}/lock", async (
            string userId,
            [FromBody] LockUserRequest request,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.LockUserAsync(userId, request.Reason);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequiresTenantAdmin()
        .WithName("LockUser")
        .WithOpenApi(operation => {
            operation.Summary = "Lock a user";
            operation.Description = "Locks a user account preventing them from accessing the system";
            return operation;
        });

        group.MapPost("/{userId}/unlock", async (
            string userId,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.UnlockUserAsync(userId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequiresTenantAdmin()
        .WithName("UnlockUser")
        .WithOpenApi(operation => {
            operation.Summary = "Unlock a user";
            operation.Description = "Unlocks a user account allowing them to access the system again";
            return operation;
        });

        group.MapGet("/{userId}/lockstatus", async (
            string userId,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.IsUserLockedOutAsync(userId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("GetUserLockStatus")
        .WithOpenApi(operation => {
            operation.Summary = "Get user lock status";
            operation.Description = "Returns whether a user account is currently locked";
            return operation;
        });

        group.MapGet("/{userId}", async (
            string userId,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.GetUserAsync(userId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("GetUser")
        .WithOpenApi(operation => {
            operation.Summary = "Get user details";
            operation.Description = "Returns detailed information about a user";
            return operation;
        });

        group.MapGet("/debug-auth", (HttpContext context) =>
        {
            var identity = context.User.Identity as ClaimsIdentity;

            Console.WriteLine($"IsInRole(SysAdmin): {context.User.IsInRole("SysAdmin")}");

            return Results.Ok(new
            {
                IsAuthenticated = identity?.IsAuthenticated,
                RoleClaimType = identity?.RoleClaimType,
                Roles = identity?.FindAll(identity.RoleClaimType ?? "").Select(c => c.Value).ToList(),
                IsInRole_SysAdmin = context.User.IsInRole("SysAdmin")
            });
        });
    }
}

public class LockUserRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; set; }
}