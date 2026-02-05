using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils;

namespace Features.WebApi.Endpoints;

public static class UserTenantEndpoints
{
    public static void MapUserTenantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/user-tenants")
            .WithTags("WebAPI - User Tenants")
            .RequiresToken();

        group.MapGet("/current", async (
            [FromServices] IUserTenantService service,
            HttpContext httpContext) =>
        {
            var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
            
            // Try Bearer token extraction first
            var (success, token) = AuthorizationHeaderHelper.ExtractBearerToken(authHeader);
            
            // If Bearer token extraction failed, try using the header value directly (fallback for non-Bearer tokens)
            if (!success || string.IsNullOrEmpty(token))
            {
                token = authHeader?.Trim();
            }
            
            // Final validation
            if (string.IsNullOrEmpty(token))
            {
                return Results.Unauthorized();
            }
            
            var result = await service.GetCurrentUserTenants(token);
            return Results.Ok(result);
        })
        .WithName("GetCurrentUserTenants")
        .RequireAuthorization("RequireTokenAuth")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get all tenants for current user";
            operation.Description = "Returns all tenant IDs assigned to current user";
            return operation;
        });

        group.MapGet("/tenantUsers", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] UserTypeFilter type,
            [FromQuery] string? search,
            [FromServices] IUserTenantService service,
            HttpContext httpContext) =>
        {
            var tenant = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            if (string.IsNullOrEmpty(tenant))
            {
                return Results.BadRequest("Tenant ID is required");
            }

            var filter = new UserFilter
            {
                Page = page,
                PageSize = pageSize,
                Type = type,
                Tenant = tenant,
                Search = search == "null" ? null : search
            };
            var result = await service.GetTenantUsers(filter);
            return Results.Ok(result);
        })
        .WithName("GetCurrentTenantUsers")
        .RequiresValidTenantAdmin()
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get all users for current tenant";
            operation.Description = "Returns all users IDs assigned to current tenant. Only tenant admins can access this.";
            return operation;
        });

        group.MapPut("/updateTenantUser", async (
           [FromBody] EditUserDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.UpdateTenantUser(dto);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("UpdateTenantUser")
        .RequiresValidTenantAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Update tenant user";
            operation.Description = "Update tenant user details (name, email, active status) and tenant-specific roles for the current tenant only. " +
                                   "Cannot modify system admin status or roles in other tenants. Only tenant admins can use this endpoint.";
            return operation;
        });

        group.MapGet("/unapprovedUsers", async (
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.GetUnapprovedUsers();
            return Results.Ok(result);
        })
        .WithName("GetUnapprovedUsers")
        .RequireAuthorization("RequireTokenAuth")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get list of unapproved user requests for the current tenant";
            operation.Description = "Returns users with pending approval for the current tenant (from X-Tenant-Id header). Same for tenant admins and system admins: only the selected tenant's requests are returned. System admins can see all unapproved users for all tenants.";
            return operation;
        })
        .RequiresValidTenantAdmin();

        group.MapPost("/approveUser", async (
            [FromBody] UserTenantDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.ApproveUser(dto.UserId, dto.TenantId, dto.IsApproved);
            return Results.Ok(result);
        })
        .WithName("ApproveUser")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Approve user";
            operation.Description = "Approve user by assigning a tenant and role to user";
            return operation;
        })
        .RequiresValidTenantAdmin();

        group.MapDelete("/", async (
            [FromBody] UserTenantDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.RemoveTenantFromUser(dto.UserId, dto.TenantId);
            return Results.Ok(result);
        })
        .WithName("RemoveTenantFromUser")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Remove a tenant from a user";
            operation.Description = "Removes a tenant assignment from a user";
            return operation;
        })
        .RequiresValidTenantAdmin();

        group.MapPost("/AddUserToCurrentTenant", async (
            [FromBody] AddUserToTenantDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.AddTenantToUserIfExist(dto.Email);
            return Results.Ok(result);
        })
        .WithName("AddUserToCurrentTenant")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Assign a tenant to a user";
            operation.Description = "Assigns a tenant to a user";
            return operation;
        })
        .RequiresValidTenantAdmin();

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
        .WithName("InviteUserToTenant")
        .WithOpenApi(operation => {
            operation.Summary = "Invite a user";
            operation.Description = "Invites a user to join a tenant and assigns roles.";
            return operation;
        });

        group.MapGet("/invitations", async (
            [FromServices] IUserManagementService userManagementService,
            HttpContext httpContext) =>
        {
            var tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            if (string.IsNullOrEmpty(tenantId))
            {
                return Results.BadRequest(new { error = "Tenant ID header is required" });
            }
            
            var result = await userManagementService.GetAllInvitationsAsync(tenantId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("GetTenantInvitations")
        .RequiresValidTenantAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Get all invitations for current tenant";
            operation.Description = "Retrieves all invitations for the current tenant specified in X-Tenant-Id header";
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
        .WithName("DeleteTenantInvitation")
        .RequiresValidTenantAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Delete a invitation";
            operation.Description = "Delete a invitation by token";
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
        .WithName("SearchUsersInTenant")
        .RequiresValidTenantAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Search users";
            operation.Description = "Search users by name, email, or other supported fields";
            return operation;
        });

        group.MapPost("/CreateNewUser", async (
            [FromBody] CreateNewUserDto dto,
            [FromServices] IUserTenantService service,
            HttpContext httpContext) =>
        {
            var tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            if (string.IsNullOrEmpty(tenantId))
            {
                return Results.BadRequest(new { error = "Tenant ID header is required" });
            }

            var result = await service.CreateNewUserInTenant(dto, tenantId);
            return result.IsSuccess
                ? Results.Created($"/api/users/{result.Data?.UserId}", result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("CreateNewUser")
        .RequiresValidTenantAdmin()
        .WithOpenApi(operation => {
            operation.Summary = "Create a new user";
            operation.Description = "Creates a new user account and assigns them to the current tenant with specified roles. " +
                                   "User email must be unique. Only tenant admins can use this endpoint.";
            return operation;
        });
    }
}