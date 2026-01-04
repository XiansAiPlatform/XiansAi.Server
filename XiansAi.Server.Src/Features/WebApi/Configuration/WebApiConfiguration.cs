using Features.WebApi.Endpoints;
using Features.WebApi.Repositories;
using Features.WebApi.Services;
using Features.WebApi.Auth;
using Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using Shared.Services;

namespace Features.WebApi.Configuration;

public static class WebApiConfiguration
{
    public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Register Web API specific services       
        builder.Services.AddScoped<IWorkflowStarterService, WorkflowStarterService>();
        builder.Services.AddScoped<IWorkflowEventsService, WorkflowEventsService>();
        builder.Services.AddScoped<IWorkflowFinderService, WorkflowFinderService>();
        builder.Services.AddScoped<IWorkflowCancelService, WorkflowCancelService>();
        builder.Services.AddScoped<ILogsService, LogsService>();
        builder.Services.AddScoped<IActivitiesService, ActivitiesService>();
        builder.Services.AddScoped<IMessagingService, MessagingService>();
        builder.Services.AddScoped<IAuditingService, AuditingService>();
        builder.Services.AddScoped<IAgentService, AgentService>();
        builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();
        builder.Services.AddScoped<ITemplateService, TemplateService>(); // Now in Shared.Services
        builder.Services.AddScoped<Features.WebApi.Services.IDocumentService, Features.WebApi.Services.DocumentService>();

        // Register schedule service
        builder.Services.AddScoped<IScheduleService, ScheduleService>();
        
        // Register task service
        builder.Services.AddScoped<ITaskService, TaskService>();

        // Register repositories
        builder.Services.AddScoped<ILogRepository, LogRepository>();
        builder.Services.AddScoped<IActivityRepository, ActivityRepository>();      

        return builder;
    }

    /// <summary>
    /// Configures authentication for WebAPI based on the AuthProvider:Provider setting.
    /// Supports "Oidc" for Generic OpenID Connect authentication, or falls back to provider-specific auth.
    /// </summary>
    public static WebApplicationBuilder AddWebApiOidcAuth(this WebApplicationBuilder builder)
    {
        var authProvider = builder.Configuration["AuthProvider:Provider"];
        var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();

        if (string.IsNullOrWhiteSpace(authProvider))
        {
            logger.LogWarning("AuthProvider:Provider is not configured. WebAPI will run without authentication.");
            return builder;
        }

        if (authProvider.Equals("Oidc", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Configuring WebAPI with Generic OIDC authentication");

            // Register Static OIDC Config Service (reads from appsettings.json)
            builder.Services.AddSingleton<ITenantOidcConfigService, StaticOidcConfigService>();

            // Register OIDC Validator (shared with User API)
            builder.Services.AddScoped<IDynamicOidcValidator, DynamicOidcValidator>();

            // Configure authentication scheme
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "OidcScheme";
                options.DefaultChallengeScheme = "OidcScheme";
            })
            .AddScheme<AuthenticationSchemeOptions, OidcAuthenticationHandler>("OidcScheme", options => { });

            // Add authorization services with policies
            builder.Services.AddAuthorization(options =>
            {
                // Policy that only validates the token
                options.AddPolicy("RequireTokenAuth", policy =>
                {
                    policy.AuthenticationSchemes.Add("OidcScheme");
                    policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.TokenOnly));
                });
                
                // Policy that validates token and tenant ID, but not tenant configuration
                options.AddPolicy("RequireTenantAuth", policy =>
                {
                    policy.AuthenticationSchemes.Add("OidcScheme");
                    policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.FullTenantValidation));
                    policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin, SystemRoles.TenantUser);
                });
                
                // Optional: Add a policy that validates token and tenant ID but not configuration
                options.AddPolicy("RequireTenantAuthWithoutConfig", policy =>
                {
                    policy.AuthenticationSchemes.Add("OidcScheme");
                    policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.TenantWithoutConfig));
                });

                //role based policies
                options.AddPolicy("RequireSysAdmin", policy =>
                {
                    policy.AuthenticationSchemes.Add("OidcScheme");
                    policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.FullTenantValidation, SystemRoles.SysAdmin));
                });

                options.AddPolicy("RequireTenantAdmin", policy =>
                {
                    policy.AuthenticationSchemes.Add("OidcScheme");
                    policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.FullTenantValidation, SystemRoles.SysAdmin, SystemRoles.TenantAdmin));
                });
            });

            // Register the unified authorization handler
            builder.Services.AddScoped<IAuthorizationHandler, AuthRequirementHandler>();

            logger.LogInformation("WebAPI Generic OIDC authentication configured successfully");
        }
        else
        {
            logger.LogInformation("Using provider-specific authentication for '{AuthProvider}'. Consider migrating to Generic OIDC (Provider: 'Oidc') for provider-agnostic authentication.", authProvider);
        }

        return builder;
    }
    
    public static WebApplication UseWebApiEndpoints(this WebApplication app)
    {
        // Map Web API endpoints
        WorkflowEndpoints.MapWorkflowEndpoints(app);
        KnowledgeEndpoints.MapKnowledgeEndpoints(app);
        LogsEndpoints.MapLogsEndpoints(app);
        SettingsEndpoints.MapSettingsEndpoints(app);
        TenantEndpoints.MapTenantEndpoints(app);
        MessagingEndpoints.MapMessagingEndpoints(app);
        WebApiSseEndpoints.MapWebApiSseEndpoints(app); // Add SSE endpoints for real-time messaging
        AuditingEndpoints.MapAuditingEndpoints(app);
        PermissionsEndpoints.MapPermissionsEndpoints(app);
        AgentEndpoints.MapAgentEndpoints(app);
        RoleManagementEndpoints.MapRoleManagementEndpoints(app);
        UserTenantEndpoints.MapUserTenantEndpoints(app);
        UserManagementEndpoints.MapUserManagementEndpoints(app);
        OidcConfigEndpoints.MapOidcConfigEndpoints(app);
        TemplateEndpoints.MapTemplateEndpoints(app);
        ScheduleEndpoints.MapScheduleEndpoints(app);
        UsageStatisticsEndpoints.MapUsageStatisticsEndpoints(app);
        DocumentEndpoints.MapDocumentEndpoints(app);
        TaskEndpoints.MapTaskEndpoints(app);

        ApiKeyEndpoints.MapApiKeyEndpoints(app);
        
        return app;
    }
} 