using Features.WebApi.Endpoints;
using Features.WebApi.Auth;
using Features.WebApi.Repositories;
using Features.WebApi.Services;
using XiansAi.Server.Features.WebApi.Repositories;
using Microsoft.AspNetCore.Authorization;
using XiansAi.Server.Features.WebApi.Endpoints;

namespace Features.WebApi.Configuration;

public static class WebApiConfiguration
{
    public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Register authorization handlers
        builder.Services.AddScoped<IAuthorizationHandler, ValidTenantHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, TokenClientHandler>();

        // Register Web API specific services
        builder.Services.AddScoped<IAuthMgtConnect, AuthMgtConnect>();
        builder.Services.AddScoped<IWorkflowStarterService, WorkflowStarterService>();
        builder.Services.AddScoped<IWorkflowEventsService, WorkflowEventsService>();
        builder.Services.AddScoped<IWorkflowFinderService, WorkflowFinderService>();
        builder.Services.AddScoped<IWorkflowCancelService, WorkflowCancelService>();
        builder.Services.AddScoped<ILogsService, LogsService>();
        builder.Services.AddScoped<ITenantService, TenantService>();
        //builder.Services.AddScoped<IWebhookService, WebhookService>();
        builder.Services.AddScoped<IActivitiesService, ActivitiesService>();
        builder.Services.AddScoped<IMessagingService, MessagingService>();
        builder.Services.AddScoped<IAuditingService, AuditingService>();
        builder.Services.AddScoped<IAgentService, AgentService>();
        builder.Services.AddScoped<IPublicService, PublicService>();
        
        // Register repositories
        builder.Services.AddScoped<ILogRepository, LogRepository>();
        builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
        //builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();
        
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
        //WebhookEndpoints.MapWebhookEndpoints(app);
        PublicEndpoints.MapPublicEndpoints(app);
        MessagingEndpoints.MapMessagingEndpoints(app);
        AuditingEndpoints.MapAuditingEndpoints(app);
        PermissionsEndpoints.MapPermissionsEndpoints(app);
        AgentEndpoints.MapAgentEndpoints(app);
        
        return app;
    }
} 