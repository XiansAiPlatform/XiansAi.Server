using Features.AdminApi.Constants;
using Features.AdminApi.Endpoints;
using Features.AdminApi.Auth;
using Features.WebApi.Services;
using Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

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
    /// AdminApi supports API key authentication (similar to UserApi) for programmatic access.
    /// </summary>
    public static WebApplicationBuilder AddAdminApiAuth(this WebApplicationBuilder builder)
    {
        // Configure authentication scheme for AdminApi endpoints
        builder.Services.AddAuthentication(options =>
        {
            // Optionally set a default scheme if desired
        })
        .AddScheme<AuthenticationSchemeOptions, AdminEndpointAuthenticationHandler>(
            "AdminEndpointApiKeyScheme", options => { });

        // Register authorization handler
        builder.Services.AddScoped<IAuthorizationHandler, ValidAdminEndpointAccessHandler>();

        // Add authorization policy for AdminApi endpoints
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminEndpointAuthPolicy", policy =>
            {
                // Important: Only use the AdminEndpointApiKeyScheme to prevent JWT authentication from running first
                // This ensures API key authentication is properly handled without JWT interference
                policy.AuthenticationSchemes.Clear();
                policy.AddAuthenticationSchemes("AdminEndpointApiKeyScheme");
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ValidAdminEndpointAccessRequirement());
            });
        });

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
        AdminOwnershipEndpoints.MapAdminOwnershipEndpoints(adminApiGroup);
        AdminTokenEndpoints.MapAdminTokenEndpoints(adminApiGroup);
        AdminKnowledgeEndpoints.MapAdminKnowledgeEndpoints(adminApiGroup);
        
        return app;
    }
}

