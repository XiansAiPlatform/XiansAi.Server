using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Shared.Utils.Services;
using Shared.Services;

namespace Features.WebApi.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this WebApplication app)
    {
        // Map tenant endpoints with common attributes
        var tenantsGroup = app.MapGroup("/api/client/tenants")
            .WithTags("WebAPI - Tenants")
            .RequiresValidTenant()
            .RequireAuthorization();
        

        tenantsGroup.MapGet("/", async (
            HttpContext httpContext,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.GetAllTenants();
            return result.ToHttpResult();
        })
        .WithName("Get Tenants")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get all tenants";
            operation.Description = "Retrieves all tenant records";
            return operation;
        }).RequiresValidSysAdmin();

        tenantsGroup.MapGet("/list", async (
            HttpContext httpContext,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.GetTenantIdList();
            return result.ToHttpResult();
        })
        .WithName("Get Tenant List")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get tenant List";
            operation.Description = "Retrieves all tenant ids";
            return operation;
        }).RequiresValidSysAdmin();

        tenantsGroup.MapGet("/currentTenantInfo", async (
            HttpContext httpContext,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.GetCurrentTenantInfo();
            return result.ToHttpResult();
        })
        .WithName("Get current Tenant info")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get current tenant info";
            operation.Description = "Retrieves current tenant information";
            return operation;
        })
        .RequiresValidTenantAdmin();

        tenantsGroup.MapPost("/", async (
            [FromBody] CreateTenantRequest request,
            [FromServices] ITenantService endpoint,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            logger.LogInformation("Endpoint received CreateTenantRequest - TenantId: {TenantId}, Name: {Name}, CreatedBy: {CreatedBy}", 
                request.TenantId, request.Name, request.CreatedBy);
            
            var result = await endpoint.CreateTenant(request);
            return result.ToHttpResult();
        })
        .WithName("Create Tenant")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Create a new tenant";
            operation.Description = "Creates a new tenant record";
            return operation;
        }).RequiresValidSysAdmin();

        tenantsGroup.MapPut("/{id}", async (
            string id,
            [FromBody] UpdateTenantRequest request,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.UpdateTenant(id, request);
            return result.ToHttpResult();
        })
        .WithName("Update Tenant")
        .WithOpenApi(operation => {
            operation.Summary = "Update a tenant";
            operation.Description = "Updates an existing tenant record";
            return operation;
        }).RequiresValidSysAdmin();

        tenantsGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.DeleteTenant(id);
            return result.ToHttpResult();
        })
        .WithName("Delete Tenant")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a tenant";
            operation.Description = "Deletes a tenant by its ID";
            return operation;
        }).RequiresValidSysAdmin();
    }
} 