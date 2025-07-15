using Features.WebApi.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Shared.Services;

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
        .RequiresValidTenantAdmin()
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
        .RequiresValidTenantAdmin()
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
        .RequiresValidTenantAdmin()
        .WithName("GetUser")
        .WithOpenApi(operation => {
            operation.Summary = "Get user details";
            operation.Description = "Returns detailed information about a user";
            return operation;
        });

        group.MapPost("/invite", async (
            [FromBody] InviteUserDto invite,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.InviteUserAsync(invite);
            return result.IsSuccess
                ? Results.Ok(new { token = result.Data })
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequiresValidTenantAdmin()
        .WithName("InviteUser")
        .WithOpenApi(operation => {
            operation.Summary = "Invite a user";
            operation.Description = "Invites a user to join a tenant and assigns roles.";
            return operation;
        });

        group.MapGet("/currentUserInvitation", async (
            [FromServices] IUserManagementService userManagementService,
            HttpContext httpContext) =>
        {
            var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
            var result = await userManagementService.GetInviteByUserEmailAsync(authHeader!.Substring("Bearer ".Length));
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("GetCurrentUserInvitation")
        .WithOpenApi(operation => {
            operation.Summary = "Get invitation of current user";
            operation.Description = "Retrieves an invitation by the current user's email address.";
            return operation;
        });

        group.MapGet("/invitations", async (
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.GetAllInvitationsAsync();
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("GetInvitations")
        .RequiresValidSysAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Get all invitation";
            operation.Description = "Retrieves all invitations";
            return operation;
        });

        group.MapPost("/accept-invitation", async (
           [FromBody]InviteDto dto,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.AcceptInvitationAsync(dto.Token);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("AcceptInvitation")
        .RequiresToken()
        .WithOpenApi(operation => {
            operation.Summary = "Accept a user invitation";
            operation.Description = "Accepts an invitation using the invitation token and creates the user account.";
            return operation;
        });
    }
}

public class LockUserRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; set; }
}