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
        
        .WithSummary("Get all tenants for current user")
        .WithDescription("Returns all tenants assigned to current user with tenantId and name fields");

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
        
        .WithSummary("Get all users for current tenant")
        .WithDescription("Returns all users IDs assigned to current tenant. Only tenant admins can access this.");

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
        
        .WithSummary("Update tenant user")
        .WithDescription("Update tenant user details (name, email) and tenant-specific roles and approval (TenantRoles[].isApproved) for the current tenant only. ");

        group.MapGet("/unapprovedUsers", async (
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.GetUnapprovedUsers();
            return Results.Ok(result);
        })
        .WithName("GetUnapprovedUsers")
        .RequireAuthorization("RequireTokenAuth")
        
        .WithSummary("Get list of unapproved user requests for the current tenant")
        .WithDescription("Returns users with pending approval for the current tenant (from X-Tenant-Id header). Same for tenant admins and system admins: only the selected tenant's requests are returned. System admins can see all unapproved users for all tenants.")
        .RequiresValidTenantAdmin();

        group.MapPost("/approveUser", async (
            [FromBody] UserTenantDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.ApproveUser(dto.UserId, dto.TenantId, dto.IsApproved);
            return Results.Ok(result);
        })
        .WithName("ApproveUser")
        
        .WithSummary("Approve user")
        .WithDescription("Approve user by assigning a tenant and role to user")
        .RequiresValidTenantAdmin();

        group.MapDelete("/", async (
            [FromBody] UserTenantDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.RemoveTenantFromUser(dto.UserId, dto.TenantId);
            return Results.Ok(result);
        })
        .WithName("RemoveTenantFromUser")
        
        .WithSummary("Remove a tenant from a user")
        .WithDescription("Removes a tenant assignment from a user")
        .RequiresValidTenantAdmin();

        group.MapPost("/AddUserToCurrentTenant", async (
            [FromBody] AddUserToTenantDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.AddTenantToUserIfExist(dto.Email);
            return Results.Ok(result);
        })
        .WithName("AddUserToCurrentTenant")
        
        .WithSummary("Assign a tenant to a user")
        .WithDescription("Assigns a tenant to a user")
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
        
        .WithSummary("Invite a user")
        .WithDescription("Invites a user to join a tenant and assigns roles.");

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
        
        .WithSummary("Get all invitations for current tenant")
        .WithDescription("Retrieves all invitations for the current tenant specified in X-Tenant-Id header");

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
        
        .WithSummary("Delete a invitation")
        .WithDescription("Delete a invitation by token");

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
        
        .WithSummary("Search users")
        .WithDescription("Search users by name, email, or other supported fields");

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
        
        .WithSummary("Create a new user")
        .WithDescription("Creates a new user account and assigns them to the current tenant with specified roles. ");
    }
}