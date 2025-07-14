using Features.WebApi.Endpoints;
using Features.WebApi.Repositories;
using Features.WebApi.Services;
using XiansAi.Server.Features.WebApi.Endpoints;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Features.WebApi.Services;
using Shared.Auth;
//using XiansAi.Server.Shared.Repositories;
//using XiansAi.Server.Shared.Services;

namespace Features.WebApi.Configuration;

public static class WebApiConfiguration
{
    public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Register Web API specific services
        builder.Services.AddScoped<IAuthMgtConnect, AuthMgtConnect>();
        builder.Services.AddScoped<IWorkflowStarterService, WorkflowStarterService>();
        builder.Services.AddScoped<IWorkflowEventsService, WorkflowEventsService>();
        builder.Services.AddScoped<IWorkflowFinderService, WorkflowFinderService>();
        builder.Services.AddScoped<IWorkflowCancelService, WorkflowCancelService>();
        builder.Services.AddScoped<ILogsService, LogsService>();
        builder.Services.AddScoped<ITenantService, TenantService>();
        builder.Services.AddScoped<IActivitiesService, ActivitiesService>();
        builder.Services.AddScoped<IMessagingService, MessagingService>();
        builder.Services.AddScoped<IAuditingService, AuditingService>();
        builder.Services.AddScoped<IAgentService, AgentService>();
        builder.Services.AddScoped<IUserTenantService, UserTenantService>();
        builder.Services.AddScoped<IUserManagementService, UserManagementService>();
        builder.Services.AddScoped<IPublicService, PublicService>();        
        builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();

        // Register repositories
        builder.Services.AddScoped<ILogRepository, LogRepository>();
        builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();        
        builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();

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
        WebhookEndpoints.MapWebhookEndpoints(app);
        PublicEndpoints.MapPublicEndpoints(app);
        MessagingEndpoints.MapMessagingEndpoints(app);
        AuditingEndpoints.MapAuditingEndpoints(app);
        PermissionsEndpoints.MapPermissionsEndpoints(app);
        AgentEndpoints.MapAgentEndpoints(app);
        RoleManagementEndpoints.MapRoleManagementEndpoints(app);
        UserTenantEndpoints.MapUserTenantEndpoints(app);
        UserManagementEndpoints.MapUserManagementEndpoints(app);

        ApiKeyEndpoints.MapApiKeyEndpoints(app);
        
        return app;
    }
} 