using Features.AdminApi.Constants;
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
    /// API versioning is implemented via URL path (e.g., /api/v1/admin/...).
    /// </summary>
    public static WebApplicationBuilder AddAdminApiServices(this WebApplicationBuilder builder)
    {
        // AdminApi reuses existing services from WebApi
        return builder;
    }

    /// <summary>
    /// Configures authentication for AdminApi.
    /// AdminApi uses the same authentication as WebApi (OIDC) but with admin role requirements.
    /// </summary>
    public static WebApplicationBuilder AddAdminApiAuth(this WebApplicationBuilder builder)
    {
        // AdminApi uses the same authentication as WebApi
        return builder;
    }

    private const string ApiVersion = "v1";

    /// <summary>
    /// Gets the base path for AdminApi endpoints.
    /// </summary>
    public static string GetBasePath() => $"/api/{ApiVersion}/admin";

    /// <summary>
    /// Maps AdminApi endpoints to the application.
    /// </summary>
    public static WebApplication UseAdminApiEndpoints(this WebApplication app)
    {
        var adminApiGroup = app.MapGroup(GetBasePath());
        
        AdminTenantEndpoints.MapAdminTenantEndpoints(adminApiGroup);
        AdminAgentEndpoints.MapAdminAgentEndpoints(adminApiGroup);
        AdminTemplateEndpoints.MapAdminTemplateEndpoints(adminApiGroup);
        AdminReportingUsersEndpoints.MapAdminReportingUsersEndpoints(adminApiGroup);
        AdminOwnershipEndpoints.MapAdminOwnershipEndpoints(adminApiGroup);
        AdminTokenEndpoints.MapAdminTokenEndpoints(adminApiGroup);
        AdminKnowledgeEndpoints.MapAdminKnowledgeEndpoints(adminApiGroup);
        
        return app;
    }
}

