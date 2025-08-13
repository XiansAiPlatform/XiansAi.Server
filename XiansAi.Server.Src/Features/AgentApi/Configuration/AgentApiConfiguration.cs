using Features.AgentApi.Repositories;
using Features.AgentApi.Services;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Auth;
using Features.AgentApi.Endpoints;
using Shared.Utils;
using Shared.Services;

namespace Features.AgentApi.Configuration;

public static class AgentApiConfiguration
{
    public static WebApplicationBuilder AddAgentApiServices(this WebApplicationBuilder builder)
    {
        // Register Agent API specific services
        builder.Services.AddScoped<ICertificateRepository, CertificateRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IActivityHistoryRepository, ActivityHistoryRepository>();
        builder.Services.AddScoped<IActivityHistoryService, ActivityHistoryService>();
        builder.Services.AddScoped<IDefinitionsService, DefinitionsService>();
        builder.Services.AddScoped<ILogsService, LogsService>();
        builder.Services.AddScoped<IObjectCacheWrapperService, ObjectCacheWrapperService>();
        builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
        builder.Services.AddScoped<IDocumentService, DocumentService>();
        
        // Register CertificateGenerator
        builder.Services.AddScoped<CertificateGenerator>();

        // Register the certificate service
        builder.Services.AddScoped<CertificateService>();

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
                policy.AuthenticationSchemes.Clear(); // Clear any inherited schemes
                policy.AuthenticationSchemes.Add(CertificateAuthenticationOptions.DefaultScheme);
                policy.RequireAuthenticatedUser();
            });
        });
        
        return builder;
    }
    
    public static WebApplication UseAgentApiEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        // Map Lib API endpoints
        CacheEndpoints.MapCacheEndpoints(app, loggerFactory);
        KnowledgeEndpoints.MapKnowledgeEndpoints(app, loggerFactory);
        ActivityHistoryEndpoints.MapActivityHistoryEndpoints(app, loggerFactory);
        DefinitionsEndpoints.MapDefinitionsEndpoints(app, loggerFactory);
        EventsEndpoints.MapEventsEndpoints(app, loggerFactory);
        ConversationEndpoints.MapConversationEndpoints(app);
        LogsEndpoints.MapLogsEndpoints(app, loggerFactory);
        SettingsEndpoints.MapSettingsEndpoints(app);
        DocumentEndpoints.MapDocumentEndpoints(app, loggerFactory);
        
        return app;
    }
} 