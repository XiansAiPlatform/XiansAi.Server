using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.Features.WebApi.Services;

namespace XiansAi.Server.Features.WebApi.Endpoints;

public static class UserTenantEndpoints
{
    public static void MapUserTenantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/user-tenants")
            .WithTags("WebAPI - User Tenants")
            .RequiresToken();

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
        });

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
        });
    }
}