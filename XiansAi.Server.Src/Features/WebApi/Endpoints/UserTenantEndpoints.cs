using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Services;

namespace XiansAi.Server.Features.WebApi.Endpoints;

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
            var result = await service.GetCurrentUserTenants(authHeader!.Substring("Bearer ".Length));
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

        group.MapGet("/{userId}", async (
            string userId,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.GetTenantsForUser(userId);
            return Results.Ok(result);
        })
        .WithName("GetUserTenants")
        .RequireAuthorization("RequireTokenAuth")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get all tenants for a user";
            operation.Description = "Returns all tenant IDs assigned to a user";
            return operation;
        })
        .RequiresValidSysAdmin();

        group.MapGet("/unapprovedUsers", async (
            [FromQuery]string? tenantId,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.GetUnapprovedUsers(tenantId);
            return Results.Ok(result);
        })
        .WithName("GetUnapprovedUsers")
        .RequireAuthorization("RequireTokenAuth")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get list of your without a tenant";
            operation.Description = "Returns all user without tenant, excluding sysAdmin users";
            return operation;
        })
        .RequiresValidSysAdmin();

        group.MapPost("/approveUser", async (
            [FromBody] UserTenantDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.ApproveUser(dto.UserId, dto.TenantId);
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

        group.MapPost("/requestJoinTenant", async (
            [FromBody] JoinTenantRequestDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.RequestToJoinTenant(dto.TenantId);
            return Results.Ok(result);
        })
        .WithName("RequestJoinTenant")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Join tenant request";
            operation.Description = "request to join given tenant";
            return operation;
        });

        group.MapPost("/", async (
            [FromBody] UserTenantDto dto,
            [FromServices] IUserTenantService service) =>
        {
            var result = await service.AddTenantToUser(dto.UserId, dto.TenantId);
            return Results.Ok(result);
        })
        .WithName("AddTenantToUser")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Assign a tenant to a user";
            operation.Description = "Assigns a tenant to a user";
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
    }
}