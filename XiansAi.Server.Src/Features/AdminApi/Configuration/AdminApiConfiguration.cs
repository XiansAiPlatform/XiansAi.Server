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
    /// Maps AdminApi endpoints to the application.
    /// All AdminApi endpoints are under /api/admin/ prefix.
    /// </summary>
    public static WebApplication UseAdminApiEndpoints(this WebApplication app)
    {
        // Map AdminApi tenant management endpoints
        AdminTenantEndpoints.MapAdminTenantEndpoints(app);
        
        // Map AdminApi agent management endpoints
        AdminAgentEndpoints.MapAdminAgentEndpoints(app);
        
        // Map AdminApi template endpoints
        AdminTemplateEndpoints.MapAdminTemplateEndpoints(app);
        
        // Map AdminApi reporting users endpoints
        AdminReportingUsersEndpoints.MapAdminReportingUsersEndpoints(app);
        
        // Map AdminApi ownership endpoints
        AdminOwnershipEndpoints.MapAdminOwnershipEndpoints(app);
        
        // Map AdminApi token endpoints
        AdminTokenEndpoints.MapAdminTokenEndpoints(app);
        
        return app;
    }
}


