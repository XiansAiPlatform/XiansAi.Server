using Features.AgentApi.Repositories;
using Features.AgentApi.Services;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Auth;
using Features.AgentApi.Endpoints;
using Shared.Utils;
using Shared.Services;
using Shared.Auth;
using Features.WebApi.Services;

namespace Features.AgentApi.Configuration;

public static class AgentApiConfiguration
{
    public static WebApplicationBuilder AddAgentApiServices(this WebApplicationBuilder builder)
    {
        // Register Agent API specific services
        builder.Services.AddScoped<ICertificateRepository, CertificateRepository>();
        builder.Services.AddScoped<Features.AgentApi.Repositories.IFlowDefinitionRepository, Features.AgentApi.Repositories.FlowDefinitionRepository>();
        builder.Services.AddScoped<IActivityHistoryRepository, ActivityHistoryRepository>();
        builder.Services.AddScoped<IActivityHistoryService, ActivityHistoryService>();
        builder.Services.AddScoped<IDefinitionsService, DefinitionsService>();
        builder.Services.AddScoped<Features.AgentApi.Services.Lib.ILogsService, Features.AgentApi.Services.Lib.LogsService>();
        builder.Services.AddScoped<IObjectCacheWrapperService, ObjectCacheWrapperService>();
        builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
        builder.Services.AddScoped<Features.AgentApi.Services.IDocumentService, Features.AgentApi.Services.DocumentService>();
        builder.Services.AddScoped<global::Shared.Repositories.IUsageEventRepository, global::Shared.Repositories.UsageEventRepository>();
        builder.Services.AddScoped<global::Shared.Repositories.IApiKeyRepository, global::Shared.Repositories.ApiKeyRepository>();
        builder.Services.AddScoped<global::Shared.Services.IAgentDeletionService, global::Shared.Services.AgentDeletionService>();
        builder.Services.AddScoped<IScheduleService, ScheduleService>();
        
        // Register CertificateGenerator
        builder.Services.AddScoped<CertificateGenerator>();

        // Register the certificate service
        builder.Services.AddScoped<CertificateService>();

        return builder;
    }
    
    public static WebApplicationBuilder AddAgentApiAuth(this WebApplicationBuilder builder)
    {
        // Register certificate validation cache with memory-based caching for performance
        // Uses IMemoryCache with size limits to prevent DoS attacks
        builder.Services.AddScoped<ICertificateValidationCache, MemoryCertificateValidationCache>();
        
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

            // Role-based policies for AgentAPI (using Certificate prefix to avoid conflicts with WebAPI)
            options.AddPolicy("RequireCertificateSysAdmin", policy =>
            {
                policy.AuthenticationSchemes.Clear();
                policy.AuthenticationSchemes.Add(CertificateAuthenticationOptions.DefaultScheme);
                policy.RequireRole(SystemRoles.SysAdmin);
            });

            options.AddPolicy("RequireCertificateTenantAdmin", policy =>
            {
                policy.AuthenticationSchemes.Clear();
                policy.AuthenticationSchemes.Add(CertificateAuthenticationOptions.DefaultScheme);
                policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin);
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
        UsageEventEndpoints.MapUsageEventEndpoints(app);
        
        return app;
    }
} 