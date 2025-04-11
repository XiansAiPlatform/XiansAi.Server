using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;

namespace Features.WebApi.Endpoints;

public static class TenantEndpointExtensions
{
    public static void MapTenantEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/tenants", async (
            [FromServices] TenantService endpoint) =>
        {
            return await endpoint.GetAllTenants();
        })
        .WithName("Get Tenants")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get all tenants";
            operation.Description = "Retrieves all tenant records";
            return operation;
        });

        app.MapGet("/api/client/tenants/{id}", async (
            string id,
            [FromServices] TenantService endpoint) =>
        {
            return await endpoint.GetTenantById(id);
        })
        .WithName("Get Tenant")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by ID";
            operation.Description = "Retrieves a tenant by its unique ID";
            return operation;
        });

        app.MapGet("/api/client/tenants/by-tenant-id/{tenantId}", async (
            string tenantId,
            [FromServices] TenantService endpoint) =>
        {
            return await endpoint.GetTenantByTenantId(tenantId);
        })
        .WithName("Get Tenant By TenantId")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by tenant ID";
            operation.Description = "Retrieves a tenant by its tenant ID";
            return operation;
        });

        app.MapGet("/api/client/tenants/by-domain/{domain}", async (
            string domain,
            [FromServices] TenantService endpoint) =>
        {
            return await endpoint.GetTenantByDomain(domain);
        })
        .WithName("Get Tenant By Domain")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by domain";
            operation.Description = "Retrieves a tenant by its domain";
            return operation;
        });

        app.MapPost("/api/client/tenants", async (
            [FromBody] CreateTenantRequest request,
            HttpContext httpContext,
            [FromServices] TenantService endpoint) =>
        {
            var userIdClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            var createdBy = userIdClaim ?? "system";
            return await endpoint.CreateTenant(request);
        })
        .WithName("Create Tenant")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Create a new tenant";
            operation.Description = "Creates a new tenant record";
            return operation;
        });

        app.MapPut("/api/client/tenants/{id}", async (
            string id,
            [FromBody] UpdateTenantRequest request,
            [FromServices] TenantService endpoint) =>
        {
            return await endpoint.UpdateTenant(id, request);
        })
        .WithName("Update Tenant")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Update a tenant";
            operation.Description = "Updates an existing tenant record";
            return operation;
        });

        app.MapDelete("/api/client/tenants/{id}", async (
            string id,
            [FromServices] TenantService endpoint) =>
        {
            return await endpoint.DeleteTenant(id);
        })
        .WithName("Delete Tenant")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a tenant";
            operation.Description = "Deletes a tenant by its ID";
            return operation;
        });
    }
} 