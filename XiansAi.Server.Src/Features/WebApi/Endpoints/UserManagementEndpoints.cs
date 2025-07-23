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

        group.MapDelete("/{userId}", async (
            string userId,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.DeleteUser(userId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequiresValidTenantAdmin()
        .WithName("DeleteUser")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a user";
            operation.Description = "Delete a user by the Id";
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

        group.MapGet("/invitations/{tenantId}", async (
            string tenantId,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.GetAllInvitationsAsync(tenantId);
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

        group.MapDelete("/invitations/{token}", async (
            string token,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.DeleteInvitation(token);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("DeleteInvitation")
        .RequiresValidTenantAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Delete a invitation";
            operation.Description = "Delete a invitation by token";
            return operation;
        });

        group.MapGet("/all", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] UserTypeFilter type,
            [FromQuery] string? tenant,
            [FromQuery] string? search,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var filter = new UserFilter
            {
                Page = page,
                PageSize = pageSize,
                Type = type,
                Tenant = tenant == "null" ? null : tenant,
                Search = search == "null" ? null : search
            };
            var result = await userManagementService.GetAllUsersAsync(filter);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("GetAllUsers")
        .RequiresValidSysAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Get all users";
            operation.Description = "Retrieves all users";
            return operation;
        });

        group.MapPut("/update", async (
           [FromBody] EditUserDto dto,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.UpdateUser(dto);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("UpdateUser")
        .RequiresToken()
        .WithOpenApi(operation => {
            operation.Summary = "Update user";
            operation.Description = "Update user details";
            return operation;
        });

        group.MapGet("/search", async (
        [FromQuery] string query,
        [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.SearchUsers(query);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("SearchUsers")
        .RequiresToken()
        .WithOpenApi(operation => {
            operation.Summary = "Search users";
            operation.Description = "Search users by name, email, or other supported fields";
            return operation;
        });

    }
}

public class LockUserRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; set; }
}