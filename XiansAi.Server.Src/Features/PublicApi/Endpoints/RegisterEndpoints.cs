using Microsoft.AspNetCore.Mvc;
using Features.PublicApi.Services;
using Shared.Utils.Services;

namespace Features.PublicApi.Endpoints;

public static class RegisterEndpoints
{
    public static void MapRegisterEndpoints(this WebApplication app)
    {
        // Map registration endpoints without authentication but with rate limiting
        var registerGroup = app.MapGroup("/api/public/register")
            .WithTags("PublicAPI - Registration");

        registerGroup.MapPost("/join-tenant", async (
            [FromBody] PublicJoinTenantRequest request,
            [FromServices] IPublicRegistrationService registrationService,
            HttpContext httpContext) =>
        {
            // Extract token from Authorization header
            var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Results.BadRequest(new { 
                    error = "Authorization header required", 
                    message = "Please provide a valid Bearer token in the Authorization header" 
                });
            }

            var userToken = authHeader.Substring("Bearer ".Length);
            var result = await registrationService.RequestToJoinTenant(request, userToken);
            return result.ToHttpResult<PublicJoinTenantResponse>();
        })
        .WithName("Request to Join Tenant")
        .WithOpenApi(operation => {
            operation.Summary = "Request to join a tenant";
            operation.Description = "Submit a request to join a specific tenant. Requires a valid JWT token in the Authorization header (Bearer <token>). The request will be pending approval from tenant administrators. Rate limited to 5 requests per minute per IP.";
            operation.RequestBody.Description = "Join tenant request containing tenant ID";
            
            // Add Authorization header parameter
            operation.Parameters ??= new List<Microsoft.OpenApi.Models.OpenApiParameter>();
            operation.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
            {
                Name = "Authorization",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Required = true,
                Description = "Bearer token for user authentication",
                Schema = new Microsoft.OpenApi.Models.OpenApiSchema
                {
                    Type = "string",
                    Example = new Microsoft.OpenApi.Any.OpenApiString("Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...")
                }
            });
            
            return operation;
        })
        .RequireRateLimiting("PublicApiRegistration");

        registerGroup.MapGet("/tenant/{tenantId}/info", (
            string tenantId) =>
        {
            // This endpoint provides basic tenant information for registration purposes
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Results.BadRequest(new { 
                    error = "Invalid tenant ID",
                    message = "Tenant ID cannot be empty"
                });
            }

            // For now, we'll create a simple validation endpoint
            // This could be expanded to show tenant details, registration requirements, etc.
            return Results.Ok(new { 
                tenantId = tenantId,
                message = "Use the join-tenant endpoint to request access to this tenant",
                endpoint = "/api/public/register/join-tenant",
                requirements = new {
                    authorization = "Valid JWT Bearer token required in Authorization header",
                    approval = "Tenant admin approval required",
                    roles = "Default role: TenantUser"
                }
            });
        })
        .WithName("Get Tenant Registration Info")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant registration information";
            operation.Description = "Get information about how to join a specific tenant. No authentication required. Rate limited to 200 requests per minute per IP.";
            return operation;
        })
        .RequireRateLimiting("PublicApiGet");

        registerGroup.MapPost("/new-tenant", async (
            [FromBody] PublicCreateTenantRequest request,
            [FromServices] IPublicRegistrationService registrationService,
            HttpContext httpContext) =>
        {
            // Extract token from Authorization header
            var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Results.BadRequest(new { 
                    error = "Authorization header required", 
                    message = "Please provide a valid Bearer token in the Authorization header" 
                });
            }

            var userToken = authHeader.Substring("Bearer ".Length);
            var result = await registrationService.CreateNewTenant(request, userToken);
            return result.ToHttpResult<PublicCreateTenantResponse>();
        })
        .WithName("Create New Tenant")
        .WithOpenApi(operation => {
            operation.Summary = "Create a new tenant";
            operation.Description = "Create a new tenant with the current user as the tenant administrator. Requires a valid JWT token in the Authorization header (Bearer <token>). The tenant ID and domain must be unique. Rate limited to 5 requests per minute per IP.";
            operation.RequestBody.Description = "New tenant request containing tenant ID, name, domain, and optional description";
            
            // Add Authorization header parameter
            operation.Parameters ??= new List<Microsoft.OpenApi.Models.OpenApiParameter>();
            operation.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
            {
                Name = "Authorization",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Required = true,
                Description = "Bearer token for user authentication",
                Schema = new Microsoft.OpenApi.Models.OpenApiSchema
                {
                    Type = "string",
                    Example = new Microsoft.OpenApi.Any.OpenApiString("Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...")
                }
            });
            
            return operation;
        })
        .RequireRateLimiting("PublicApiRegistration");
    }
}
