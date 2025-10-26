using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Shared.Services;
using Features.Shared.Configuration;
using Shared.Utils;

namespace Features.WebApi.Endpoints;

public static class UserManagementEndpoints
{
    public static void MapUserManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("WebAPI - User Management")
            .RequireAuthorization()
            .WithGlobalRateLimit(); // Apply standard rate limiting

        group.MapDelete("/{userId}", async (
            string userId,
            [FromServices] IUserManagementService userManagementService) =>
        {
            var result = await userManagementService.DeleteUser(userId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequiresValidSysAdmin()
        .WithName("DeleteUser")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a user";
            operation.Description = "Delete a user by the Id";
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
        .RequiresValidSysAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Update user";
            operation.Description = "Update user details";
            return operation;
        });
    }
}

public class LockUserRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; set; }
}