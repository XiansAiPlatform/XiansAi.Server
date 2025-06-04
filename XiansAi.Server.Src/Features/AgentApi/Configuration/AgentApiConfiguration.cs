using Features.AgentApi.Endpoints;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Auth;
using XiansAi.Server.Features.AgentApi.Repositories;
using XiansAi.Server.Features.AgentApi.Services.Agent;
using Features.AgentApi.Endpoints.V1;
using Features.AgentApi.Endpoints.V2;
using Features.AgentApi.Repositories;
using Shared.Services;

namespace Features.AgentApi.Configuration;

public static class AgentApiConfiguration
{
    public static WebApplicationBuilder AddAgentApiServices(this WebApplicationBuilder builder)
    {
        // Register repositories
        builder.Services.AddScoped<IActivityHistoryRepository, ActivityHistoryRepository>();
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<ICertificateRepository, CertificateRepository>();
        // Register HttpClient for webhook service
        builder.Services.AddHttpClient();

        // Register Lib API specific services
        builder.Services.AddScoped<CertificateService>();
        builder.Services.AddScoped<IActivityHistoryService, ActivityHistoryService>();
        builder.Services.AddScoped<IDefinitionsService, DefinitionsService>();
        builder.Services.AddScoped<IObjectCacheWrapperService, ObjectCacheWrapperService>();
        builder.Services.AddScoped<IWebhookService, WebhookService>();
        builder.Services.AddScoped<ILogsService, LogsService>();

        return builder;
    }
    
    public static WebApplicationBuilder AddAgentApiAuth(this WebApplicationBuilder builder)
    {
        // Register certificate validation cache - using no-op implementation to disable caching
        builder.Services.AddScoped<ICertificateValidationCache, NoOpCertificateValidationCache>();
        
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
    
    public static WebApplication UseAgentApiEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        // Map Lib API endpoints

        //V1
        CacheEndpointsV1.MapCacheEndpoints(app, loggerFactory);
        KnowledgeEndpointsV1.MapKnowledgeEndpoints(app, loggerFactory);
        ActivityHistoryEndpointsV1.MapActivityHistoryEndpoints(app, loggerFactory);
        DefinitionsEndpointsV1.MapDefinitionsEndpoints(app, loggerFactory);
        EventsEndpointsV1.MapEventsEndpoints(app, loggerFactory);
        WebhookEndpointsV1.MapWebhookEndpoints(app);
        ConversationEndpointsV1.MapConversationEndpoints(app);
        LogsEndpointsV1.MapLogsEndpoints(app, loggerFactory);
        SettingsEndpointsV1.MapSettingsEndpoints(app);

        //V2
        CacheEndpointsV2.MapCacheEndpoints(app, loggerFactory);
        KnowledgeEndpointsV2.MapKnowledgeEndpoints(app, loggerFactory);
        ActivityHistoryEndpointsV2.MapActivityHistoryEndpoints(app, loggerFactory);
        DefinitionsEndpointsV2.MapDefinitionsEndpoints(app, loggerFactory);
        EventsEndpointsV2.MapEventsEndpoints(app, loggerFactory);
        WebhookEndpointsV2.MapWebhookEndpoints(app);
        ConversationEndpointsV2.MapConversationEndpoints(app);
        LogsEndpointsV2.MapLogsEndpoints(app, loggerFactory);
        SettingsEndpointsV2.MapSettingsEndpoints(app);
        
        return app;
    }
} 