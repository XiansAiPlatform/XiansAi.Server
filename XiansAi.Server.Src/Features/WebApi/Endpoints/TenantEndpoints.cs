using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;
using Shared.Utils.Services;
using System.Security.Claims;

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
            var result = await endpoint.GetTenantList();
            return result.ToHttpResult();
        })
        .WithName("Get Tenant List")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get tenant List";
            operation.Description = "Retrieves all tenant ids";
            return operation;
        }).RequiresValidSysAdmin();

        tenantsGroup.MapGet("/{id}", async (
            string id,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.GetTenantByTenantId(id);
            return result.ToHttpResult();
        })
        .WithName("Get Tenant")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get tenant by ID";
            operation.Description = "Retrieves a tenant by its unique ID";
            return operation;
        })
        .RequiresValidTenantAdmin();

        tenantsGroup.MapGet("/by-tenant-id/{tenantId}", async (
            HttpContext httpContext,
            string tenantId,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.GetTenantById(tenantId);
            return result.ToHttpResult();
        })
        .WithName("Get Tenant By TenantId")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get tenant by tenant ID";
            operation.Description = "Retrieves a tenant by its tenant ID";
            return operation;
        });

        tenantsGroup.MapGet("/by-domain/{domain}", async (
            string domain,
            [FromServices] ITenantService endpoint) =>
        {
            var result = await endpoint.GetTenantByDomain(domain);
            return result.ToHttpResult();
        })
        .WithName("Get Tenant By Domain")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by domain";
            operation.Description = "Retrieves a tenant by its domain";
            return operation;
        })
        .RequiresValidTenantAdmin();

        tenantsGroup.MapPost("/", async (
            [FromBody] CreateTenantRequest request,
            HttpContext httpContext,
            [FromServices] ITenantService endpoint) =>
        {
            var userIdClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            var createdBy = userIdClaim ?? "system";
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