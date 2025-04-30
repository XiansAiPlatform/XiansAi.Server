using Features.WebApi.Endpoints;
using Features.WebApi.Auth;
using Features.WebApi.Repositories;
using Features.WebApi.Services;
using XiansAi.Server.Features.WebApi.Repositories;
using Microsoft.AspNetCore.Authorization;

namespace Features.WebApi.Configuration;

public static class WebApiConfiguration
{
    public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Register authorization handlers
        builder.Services.AddScoped<IAuthorizationHandler, ValidTenantHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, Auth0ClientHandler>();

        // Register Web API specific services
        builder.Services.AddSingleton<IAuth0MgtAPIConnect, Auth0MgtAPIConnect>();
        builder.Services.AddScoped<WorkflowStarterService>();
        builder.Services.AddScoped<WorkflowEventsService>();
        builder.Services.AddScoped<IWorkflowFinderService, WorkflowFinderService>();
        builder.Services.AddScoped<WorkflowCancelService>();
        builder.Services.AddScoped<CertificateService>();
        builder.Services.AddScoped<InstructionsService>();
        builder.Services.AddScoped<LogsService>();
        builder.Services.AddScoped<DefinitionsService>();
        builder.Services.AddScoped<TenantService>();
        builder.Services.AddScoped<WebhookService>();
        builder.Services.AddScoped<ActivitiesService>();
        builder.Services.AddScoped<IMessagingService, MessagingService>();
        builder.Services.AddScoped<IAuditingService, AuditingService>();
        
        // Register repositories
        builder.Services.AddScoped<IInstructionRepository, InstructionRepository>();
        builder.Services.AddScoped<ILogRepository, LogRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();
        
        return builder;
    }
    
    public static WebApplication UseWebApiEndpoints(this WebApplication app)
    {
        // Map Web API endpoints
        WorkflowEndpoints.MapWorkflowEndpoints(app);
        InstructionEndpoints.MapInstructionEndpoints(app);
        LogsEndpoints.MapLogsEndpoints(app);
        ActivityEndpoints.MapActivityEndpoints(app);
        SettingsEndpoints.MapSettingsEndpoints(app);
        DefinitionsEndpoints.MapDefinitionsEndpoints(app);
        TenantEndpoints.MapTenantEndpoints(app);
        WebhookEndpoints.MapWebhookEndpoints(app);
        PublicEndpoints.MapPublicEndpoints(app);
        MessagingEndpoints.MapMessagingEndpoints(app);
        AuditingEndpoints.MapAuditingEndpoints(app);
        
        return app;
    }
} 