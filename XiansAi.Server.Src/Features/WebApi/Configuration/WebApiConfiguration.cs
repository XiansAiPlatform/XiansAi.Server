using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Features.WebApi.Endpoints;
using Features.WebApi.Auth;
using Features.WebApi.Repositories;
using Features.WebApi.Services;
using XiansAi.Server.Features.WebApi.Repositories;

namespace Features.WebApi.Configuration;

public static class WebApiConfiguration
{
    public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Register Web API specific services
        builder.Services.AddSingleton<IAuth0MgtAPIConnect, Auth0MgtAPIConnect>();
        builder.Services.AddScoped<WorkflowStarterService>();
        builder.Services.AddScoped<WorkflowEventsService>();
        builder.Services.AddScoped<WorkflowFinderService>();
        builder.Services.AddScoped<WorkflowCancelService>();
        builder.Services.AddScoped<CertificateService>();
        builder.Services.AddScoped<InstructionsService>();
        builder.Services.AddScoped<DefinitionsService>();
        builder.Services.AddScoped<TenantService>();
        builder.Services.AddScoped<WebhookService>();
        builder.Services.AddScoped<ActivitiesService>();
        
        // Register repositories
        builder.Services.AddScoped<IInstructionRepository, InstructionRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();
        
        return builder;
    }
    
    public static WebApplication UseWebApiEndpoints(this WebApplication app)
    {
        // Map Web API endpoints
        WorkflowEndpointExtensions.MapWorkflowEndpoints(app);
        InstructionEndpointExtensions.MapInstructionEndpoints(app);
        ActivityEndpointExtensions.MapActivityEndpoints(app);
        SettingsEndpointExtensions.MapSettingsEndpoints(app);
        DefinitionsEndpointExtensions.MapDefinitionsEndpoints(app);
        TenantEndpointExtensions.MapTenantEndpoints(app);
        WebhookEndpointExtensions.MapWebhookEndpoints(app);
        PublicEndpointExtensions.MapPublicEndpoints(app);
        
        return app;
    }
} 