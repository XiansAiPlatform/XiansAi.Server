using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this WebApplication app)
    {
        // Map tenant endpoints with common attributes
        var tenantsGroup = app.MapGroup("/api/client/tenants")
            .WithTags("WebAPI - Tenants")
            .RequiresValidTenant();

        tenantsGroup.MapGet("/", async (
            [FromServices] ITenantService endpoint) =>
        {
            return await endpoint.GetAllTenants();
        })
        .WithName("Get Tenants")
        .WithOpenApi(operation => {
            operation.Summary = "Get all tenants";
            operation.Description = "Retrieves all tenant records";
            return operation;
        });

        tenantsGroup.MapGet("/{id}", async (
            string id,
            [FromServices] ITenantService endpoint) =>
        {
            return await endpoint.GetTenantById(id);
        })
        .WithName("Get Tenant")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by ID";
            operation.Description = "Retrieves a tenant by its unique ID";
            return operation;
        });

        tenantsGroup.MapGet("/by-tenant-id/{tenantId}", async (
            string tenantId,
            [FromServices] ITenantService endpoint) =>
        {
            return await endpoint.GetTenantByTenantId(tenantId);
        })
        .WithName("Get Tenant By TenantId")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by tenant ID";
            operation.Description = "Retrieves a tenant by its tenant ID";
            return operation;
        });

        tenantsGroup.MapGet("/by-domain/{domain}", async (
            string domain,
            [FromServices] ITenantService endpoint) =>
        {
            return await endpoint.GetTenantByDomain(domain);
        })
        .WithName("Get Tenant By Domain")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by domain";
            operation.Description = "Retrieves a tenant by its domain";
            return operation;
        });

        tenantsGroup.MapPost("/", async (
            [FromBody] CreateTenantRequest request,
            HttpContext httpContext,
            [FromServices] ITenantService endpoint) =>
        {
            var userIdClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            var createdBy = userIdClaim ?? "system";
            return await endpoint.CreateTenant(request);
        })
        .WithName("Create Tenant")
        .WithOpenApi(operation => {
            operation.Summary = "Create a new tenant";
            operation.Description = "Creates a new tenant record";
            return operation;
        });

        tenantsGroup.MapPut("/{id}", async (
            string id,
            [FromBody] UpdateTenantRequest request,
            [FromServices] ITenantService endpoint) =>
        {
            return await endpoint.UpdateTenant(id, request);
        })
        .WithName("Update Tenant")
        .WithOpenApi(operation => {
            operation.Summary = "Update a tenant";
            operation.Description = "Updates an existing tenant record";
            return operation;
        });

        tenantsGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] ITenantService endpoint) =>
        {
            return await endpoint.DeleteTenant(id);
        })
        .WithName("Delete Tenant")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a tenant";
            operation.Description = "Deletes a tenant by its ID";
            return operation;
        });
    }
} 