using Features.AdminApi.Constants;
using Features.AdminApi.Endpoints;
using Features.AdminApi.Auth;
using Features.AdminApi.Utils;
using Features.AgentApi.Repositories;
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
        // Register AdminApi specific services
        builder.Services.AddScoped<IAdminAgentService, AdminAgentService>();
        builder.Services.AddScoped<IActivationService, ActivationService>();
        builder.Services.AddScoped<IActivationCleanupService, ActivationCleanupService>();
        builder.Services.AddScoped<IAdminStatsService, AdminStatsService>();
        builder.Services.AddScoped<IAdminLogsService, AdminLogsService>();
        builder.Services.AddScoped<IAdminMetricsService, AdminMetricsService>();
        builder.Services.AddScoped<IAdminDataService, AdminDataService>();
        builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
        // Note: IAdminTaskService is registered in SharedServices as it's shared with WebApi
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

    /// <summary>
    /// Configures AdminApi middleware (debug logging, etc.)
    /// Should be called before UseAdminApiEndpoints.
    /// </summary>
    public static WebApplication UseAdminApiMiddleware(this WebApplication app)
    {
        // Add debug logging middleware (only active when AdminApi:EnableDebugLogging = true)
        app.UseMiddleware<AdminApiDebugLoggingMiddleware>();
        
        return app;
    }

    /// <summary>
    /// Maps AdminApi endpoints to the application.
    /// Supports multiple API versions (v1, v2, etc.) following the versioning guide pattern.
    /// </summary>
    public static WebApplication UseAdminApiEndpoints(this WebApplication app)
    {
        // Map v1 endpoints (current version)
        MapAdminApiVersion(app, AdminApiConstants.CurrentVersion);
        
        return app;
    }

    /// <summary>
    /// Maps AdminApi endpoints for a specific version.
    /// </summary>
    /// <param name="app">The web application</param>
    /// <param name="version">API version (e.g., "v1", "v2")</param>
    private static void MapAdminApiVersion(WebApplication app, string version)
    {
        var adminApiGroup = app.MapGroup(AdminApiConstants.GetVersionedBasePath(version));
        
        AdminTenantEndpoints.MapAdminTenantEndpoints(adminApiGroup);
        AdminAgentDeploymentEndpoints.MapAdminAgentDeploymentsEndpoints(adminApiGroup);
        AdminAgentActivationEndpoints.MapAdminAgentActivationEndpoints(adminApiGroup);
        AdminTemplateEndpoints.MapAdminTemplateEndpoints(adminApiGroup);
        AdminOwnershipEndpoints.MapAdminOwnershipEndpoints(adminApiGroup);
        AdminKnowledgeEndpoints.MapAdminKnowledgeEndpoints(adminApiGroup);
        WorkflowManagementEndpoints.MapWorkflowManagementEndpoints(adminApiGroup);
        AdminMessagingEndpoints.MapAdminMessagingEndpoints(adminApiGroup);
        AdminTaskEndpoints.MapAdminTaskEndpoints(adminApiGroup);
        AdminParticipantsEndpoints.MapAdminParticipantsEndpoints(adminApiGroup);
        AdminStatsEndpoints.MapAdminStatsEndpoints(adminApiGroup);
        AdminLogsEndpoints.MapAdminLogsEndpoints(adminApiGroup);
        AdminMetricsEndpoints.MapAdminMetricsEndpoints(adminApiGroup);
        AdminDataEndpoints.MapAdminDataEndpoints(adminApiGroup);
        AdminMigrationEndpoints.MapAdminMigrationEndpoints(adminApiGroup);
    }
}




