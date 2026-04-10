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
        
        .WithSummary("Get all tenants")
        .WithDescription("Retrieves all tenant records").RequiresValidSysAdmin();

        tenantsGroup.MapGet("/list", async (
            HttpContext httpContext,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.GetTenantIdList();
            return result.ToHttpResult();
        })
        .WithName("Get Tenant List")
        
        .WithSummary("Get tenant List")
        .WithDescription("Retrieves all tenant ids").RequiresValidSysAdmin();

        tenantsGroup.MapGet("/currentTenantInfo", async (
            HttpContext httpContext,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.GetCurrentTenantInfo(httpContext.RequestAborted);
            return result.ToHttpResult();
        })
        .WithName("Get current Tenant info")
        
        .WithSummary("Get current tenant info")
        .WithDescription("Retrieves current tenant information")
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
        
        .WithSummary("Create a new tenant")
        .WithDescription("Creates a new tenant record").RequiresValidSysAdmin();

        tenantsGroup.MapPut("/{id}", async (
            string id,
            [FromBody] UpdateTenantRequest request,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.UpdateTenant(id, request);
            return result.ToHttpResult();
        })
        .WithName("Update Tenant")
        
        .WithSummary("Update a tenant")
        .WithDescription("Updates an existing tenant record").RequiresValidSysAdmin();

        tenantsGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.DeleteTenant(id);
            return result.ToHttpResult();
        })
        .WithName("Delete Tenant")
        
        .WithSummary("Delete a tenant")
        .WithDescription("Deletes a tenant by its ID").RequiresValidSysAdmin();
    }
} 