using Features.AdminApi.Constants;
using Features.AdminApi.Endpoints;
using Features.AdminApi.Services;
using Features.AdminApi.Auth;
using Features.AdminApi.Repositories;
using Features.AdminApi.Utils;
using Features.AgentApi.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.Configuration;
using Shared.Repositories;
using Shared.Services;
using Shared.Services.HealthChecks;
using Features.Mcp.Configuration;
using Features.UserApi.Services;

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
        builder.Services.AddXiansMcp();
        // Register AdminApi specific services
        builder.Services.AddScoped<IAdminAgentService, AdminAgentService>();
        builder.Services.AddScoped<IActivationService, ActivationService>();
        builder.Services.AddScoped<IActivationCleanupService, ActivationCleanupService>();
        builder.Services.AddScoped<IAdminStatsService, AdminStatsService>();
        builder.Services.AddScoped<IFeedbackQueryService, FeedbackQueryService>();
        builder.Services.AddScoped<IAdminLogsService, AdminLogsService>();
        builder.Services.AddScoped<IAdminAuditLogService, AdminAuditLogService>();
        builder.Services.AddScoped<IAdminMetricsService, AdminMetricsService>();
        builder.Services.AddScoped<IAdminDataService, AdminDataService>();
        builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();

        // Note: IAppIntegrationRepository and IAppIntegrationService are registered
        // centrally in SharedConfiguration (shared across AppsApi, AdminApi, AgentApi).

        // Register Admin API key service
        builder.Services.AddScoped<IAdminApiKeyService, AdminApiKeyService>();


        builder.Services.AddScoped<ICapabilityMatrixRepository, CapabilityMatrixRepository>();
        builder.Services.AddScoped<ICapabilityMatrixService, CapabilityMatrixService>();

        // Register platform bootstrap service
        builder.Services.AddScoped<IBootstrapService, BootstrapService>();

        // Register Temporal Worker Deployment Version management
        builder.Services.AddScoped<IWorkerDeploymentService, WorkerDeploymentService>();
        
        // PendingRequestService for heartbeat sync flow (TryAdd: UserApi may already register it)
        builder.Services.TryAddSingleton<IPendingRequestService, PendingRequestService>();

        AddHealthCheckServices(builder);

        // Note: IAdminTaskService is registered in SharedServices as it's shared with WebApi
        // AdminApi reuses existing services from WebApi
        return builder;
    }

    /// <summary>
    /// Automated agent health checks: CRUD/history services, the per-type executors, and the
    /// Temporal worker + reconciler that run them on the platform namespace.
    /// </summary>
    private static void AddHealthCheckServices(WebApplicationBuilder builder)
    {
        builder.Services.Configure<HealthChecksOptions>(builder.Configuration.GetSection(HealthChecksOptions.SectionName));
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<HealthChecksOptions>>().Value);

        builder.Services.AddScoped<IAgentHealthCheckRepository, AgentHealthCheckRepository>();
        builder.Services.AddScoped<AgentHealthCheckService>();
        builder.Services.AddScoped<IAgentHealthCheckService>(sp => sp.GetRequiredService<AgentHealthCheckService>());
        builder.Services.AddScoped<IHealthCheckActivationHooks>(sp => sp.GetRequiredService<AgentHealthCheckService>());
        builder.Services.AddScoped<IHealthCheckRunner, HealthCheckRunner>();
        builder.Services.AddScoped<IHealthCheckNotifier, HealthCheckNotifier>();
        builder.Services.AddScoped<IHealthCheckReconciler, HealthCheckReconciler>();
        builder.Services.AddScoped<IWebhookTargetDiscovery, WebhookTargetDiscovery>();
        builder.Services.AddScoped<IHealthCheckExecutor, ChatHealthCheckExecutor>();
        builder.Services.AddScoped<IHealthCheckExecutor, WebhookHealthCheckExecutor>();
        builder.Services.AddScoped<IHealthCheckExecutor, ScheduleHealthCheckExecutor>();

        // The webhook executor drives the inbound webhook path. TryAdd: UserApi registers it too
        // when it runs in the same process.
        builder.Services.TryAddScoped<IWebhookReceiverService, WebhookReceiverService>();

        // TryAdd so tests can substitute fakes that don't need a Temporal server.
        builder.Services.TryAddSingleton<IHealthCheckTemporalClientProvider, HealthCheckTemporalClientProvider>();
        builder.Services.TryAddSingleton<IHealthCheckScheduler, HealthCheckScheduler>();
        builder.Services.AddHostedService<HealthCheckWorkerService>();
    }

    /// <summary>
    /// Configures authentication for AdminApi.
    /// AdminApi supports API key authentication (similar to UserApi) for programmatic access.
    /// </summary>
    public static WebApplicationBuilder AddAdminApiAuth(this WebApplicationBuilder builder)
    {
        // Register shared admin role/tenant resolver (used by both auth and authorization handlers)
        builder.Services.AddScoped<IAdminRoleTenantResolver, AdminRoleTenantResolver>();

        // Resolver for ID-token-only AdminApi callers.
        builder.Services.AddScoped<IAdminKeylessUserResolver, AdminKeylessUserResolver>();

        // Register the Mongo-backed OIDC service directly so admin components always use
        // the per-tenant configuration, regardless of which service is registered for the interface.
        builder.Services.AddScoped<global::Shared.Services.TenantOidcConfigService>();

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
        app.MapXiansMcp();
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
        
        AdminBootstrapEndpoints.MapAdminBootstrapEndpoints(adminApiGroup);
        AdminHeartbeatEndpoints.MapAdminHeartbeatEndpoints(adminApiGroup);
        AdminHealthCheckEndpoints.MapAdminHealthCheckEndpoints(adminApiGroup);
        AdminTenantEndpoints.MapAdminTenantEndpoints(adminApiGroup);
        AdminConsoleOidcEndpoints.MapAdminConsoleOidcEndpoints(adminApiGroup);
        AdminCapabilityMatrixEndpoints.MapAdminCapabilityMatrixEndpoints(adminApiGroup);
        AdminAgentDeploymentEndpoints.MapAdminAgentDeploymentsEndpoints(adminApiGroup);
        AdminAgentActivationEndpoints.MapAdminAgentActivationEndpoints(adminApiGroup);
        AdminTemplateEndpoints.MapAdminTemplateEndpoints(adminApiGroup);
        AdminOwnershipEndpoints.MapAdminOwnershipEndpoints(adminApiGroup);
        AdminAgentAccessEndpoints.MapAdminAgentAccessEndpoints(adminApiGroup);
        AdminKnowledgeEndpoints.MapAdminKnowledgeEndpoints(adminApiGroup);
        WorkflowManagementEndpoints.MapWorkflowManagementEndpoints(adminApiGroup);
        AdminMessagingEndpoints.MapAdminMessagingEndpoints(adminApiGroup);
        AdminFeedbackEndpoints.MapAdminFeedbackEndpoints(adminApiGroup);
        AdminTaskEndpoints.MapAdminTaskEndpoints(adminApiGroup);
        AdminScheduleEndpoints.MapAdminScheduleEndpoints(adminApiGroup);
        AdminWorkerDeploymentEndpoints.MapAdminWorkerDeploymentEndpoints(adminApiGroup);
        AdminParticipantsEndpoints.MapAdminParticipantsEndpoints(adminApiGroup);
        AdminUserEndpoints.MapAdminUserEndpoints(adminApiGroup);
        AdminContactEndpoints.MapAdminContactEndpoints(adminApiGroup);
        AdminGlobalUserEndpoints.MapAdminGlobalUserEndpoints(adminApiGroup);
        AdminStatsEndpoints.MapAdminStatsEndpoints(adminApiGroup);
        AdminLogsEndpoints.MapAdminLogsEndpoints(adminApiGroup);
        AdminAuditLogEndpoints.MapAdminAuditLogEndpoints(adminApiGroup);
        AdminMetricsEndpoints.MapAdminMetricsEndpoints(adminApiGroup);
        AdminDataEndpoints.MapAdminDataEndpoints(adminApiGroup);
        AdminAppIntegrationEndpoints.MapAdminAppIntegrationEndpoints(adminApiGroup);
        AdminSecretVaultEndpoints.MapAdminSecretVaultEndpoints(adminApiGroup);
        AdminApiKeyEndpoints.MapAdminApiKeyEndpoints(adminApiGroup);
    }
}


