using Features.AgentApi.Endpoints;
using Features.AgentApi.Services.Agent;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Auth;
using Features.AgentApi.Repositories;
using XiansAi.Server.Utils;
using XiansAi.Server.Features.AgentApi.Repositories;
using XiansAi.Server.Features.AgentApi.Services.Agent;
using XiansAi.Server.Features.AgentApi.Endpoints;
using XiansAi.Server.Shared.Data;
namespace Features.AgentApi.Configuration;

public static class AgentApiConfiguration
{
    public static WebApplicationBuilder AddAgentApiServices(this WebApplicationBuilder builder)
    {
        // Register repositories
        builder.Services.AddScoped<IActivityHistoryRepository, ActivityHistoryRepository>();
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();

        // Register HttpClient for webhook service
        builder.Services.AddHttpClient();

        // Register Lib API specific services
        builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
        builder.Services.AddScoped<IActivityHistoryService, ActivityHistoryService>();
        builder.Services.AddScoped<IDefinitionsService, DefinitionsService>();
        builder.Services.AddScoped<IWorkflowSignalService, WorkflowSignalService>();
        builder.Services.AddScoped<IObjectCacheWrapperService, ObjectCacheWrapperService>();
        builder.Services.AddScoped<IWebhookService, WebhookService>();

        return builder;
    }
    
    public static WebApplicationBuilder AddAgentApiAuth(this WebApplicationBuilder builder)
    {
        // Add certificate authentication scheme
        builder.Services.AddAuthentication()
            .AddScheme<CertificateAuthenticationOptions, CertificateAuthenticationHandler>(
                CertificateAuthenticationOptions.DefaultScheme, opts => { 
                    opts.TimeProvider = TimeProvider.System;
                });
            
        // Add certificate authentication policy
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireCertificate", policy =>
            {
                policy.AuthenticationSchemes.Add(CertificateAuthenticationOptions.DefaultScheme);
                policy.RequireAuthenticatedUser();
            });
        });
        
        return builder;
    }
    
    public static WebApplication UseLibApiEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        // Map Lib API endpoints
        CacheEndpoints.MapCacheEndpoints(app, loggerFactory);
        KnowledgeEndpoints.MapKnowledgeEndpoints(app, loggerFactory);
        ActivityHistoryEndpoints.MapActivityHistoryEndpoints(app, loggerFactory);
        DefinitionsEndpoints.MapDefinitionsEndpoints(app, loggerFactory);
        SignalEndpoints.MapSignalEndpoints(app, loggerFactory);
        WebhookEndpoints.MapWebhookEndpoints(app);
        
        return app;
    }
} 