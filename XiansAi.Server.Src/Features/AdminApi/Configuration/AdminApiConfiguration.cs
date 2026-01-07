using Features.AdminApi.Endpoints;
using Features.WebApi.Services;
using Shared.Services;

namespace Features.AdminApi.Configuration;

/// <summary>
/// Configuration for AdminApi - administrative operations separate from WebApi (UI)
/// </summary>
public static class AdminApiConfiguration
{
    /// <summary>
    /// Adds AdminApi services to the service collection.
    /// AdminApi reuses existing services but provides admin-specific endpoints.
    /// </summary>
    public static WebApplicationBuilder AddAdminApiServices(this WebApplicationBuilder builder)
    {
        // AdminApi reuses existing services from WebApi
        // These are already registered in WebApiConfiguration.AddWebApiServices()
        // No additional service registration needed unless we add AdminApi-specific services
        
        return builder;
    }

    /// <summary>
    /// Configures authentication for AdminApi.
    /// AdminApi uses the same authentication as WebApi (OIDC) but with admin role requirements.
    /// </summary>
    public static WebApplicationBuilder AddAdminApiAuth(this WebApplicationBuilder builder)
    {
        // AdminApi uses the same authentication as WebApi
        // Admin-specific authorization is handled at the endpoint level
        // No additional auth configuration needed
        
        return builder;
    }

    /// <summary>
    /// The current API version for AdminApi endpoints.
    /// Change this value to update all AdminApi endpoints to a new version.
    /// </summary>
    private const string ApiVersion = "v1";

    /// <summary>
    /// Gets the base path for AdminApi endpoints with the current version.
    /// Used for constructing absolute URLs in responses (e.g., Results.Created).
    /// </summary>
    public static string GetBasePath() => $"/api/{ApiVersion}/admin";

    /// <summary>
    /// Maps AdminApi endpoints to the application.
    /// All AdminApi endpoints are under /api/v{version}/admin/ prefix (versioned).
    /// </summary>
    public static WebApplication UseAdminApiEndpoints(this WebApplication app)
    {
        // Create a versioned route group for all AdminApi endpoints
        // This automatically prefixes all child routes with /api/v{version}/admin
        var adminApiGroup = app.MapGroup($"/api/{ApiVersion}/admin");

        // Map AdminApi tenant management endpoints
        AdminTenantEndpoints.MapAdminTenantEndpoints(adminApiGroup);
        
        // Map AdminApi agent management endpoints
        AdminAgentEndpoints.MapAdminAgentEndpoints(adminApiGroup);
        
        // Map AdminApi template endpoints
        AdminTemplateEndpoints.MapAdminTemplateEndpoints(adminApiGroup);
        
        // Map AdminApi reporting users endpoints
        AdminReportingUsersEndpoints.MapAdminReportingUsersEndpoints(adminApiGroup);
        
        // Map AdminApi ownership endpoints
        AdminOwnershipEndpoints.MapAdminOwnershipEndpoints(adminApiGroup);
        
        // Map AdminApi token endpoints
        AdminTokenEndpoints.MapAdminTokenEndpoints(adminApiGroup);
        
        return app;
    }
}


