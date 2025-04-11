using Features.AgentApi.Endpoints;
using Features.AgentApi.Services.Agent;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Auth;
using Features.AgentApi.Repositories;
using XiansAi.Server.Utils;
using Microsoft.Extensions.Logging;
using XiansAi.Server.Features.AgentApi.Repositories;
using XiansAi.Server.Features.AgentApi.Services.Agent;
using XiansAi.Server.Features.AgentApi.Endpoints;
namespace Features.AgentApi.Configuration;

public static class LibApiConfiguration
{
    public static WebApplicationBuilder AddLibApiServices(this WebApplicationBuilder builder)
    {
        // Register repositories
        builder.Services.AddScoped<ActivityHistoryRepository>(sp => 
        {
            var dbService = sp.GetRequiredService<IDatabaseService>();
            return new ActivityHistoryRepository(dbService.GetDatabase().GetAwaiter().GetResult(), 
                sp.GetRequiredService<IBackgroundTaskService>(), sp.GetRequiredService<ILogger<ActivityHistoryRepository>>());
        });

        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>(sp => 
        {
            var dbService = sp.GetRequiredService<IDatabaseService>();
            return new WebhookRepository(dbService.GetDatabase().GetAwaiter().GetResult());
        });

        builder.Services.AddScoped<FlowDefinitionRepository>(sp => 
        {
            var dbService = sp.GetRequiredService<IDatabaseService>();
            return new FlowDefinitionRepository(dbService.GetDatabase().GetAwaiter().GetResult());
        });

        builder.Services.AddScoped<KnowledgeRepository>(sp => 
        {
            var dbService = sp.GetRequiredService<IDatabaseService>();
            return new KnowledgeRepository(dbService.GetDatabase().GetAwaiter().GetResult());
        });

        // Register HttpClient for webhook service
        builder.Services.AddHttpClient();

        // Register Lib API specific services
        builder.Services.AddScoped<KnowledgeService>();
        builder.Services.AddScoped<ActivityHistoryService>();
        builder.Services.AddScoped<DefinitionsService>();
        builder.Services.AddScoped<WorkflowSignalService>();
        builder.Services.AddScoped<ObjectCacheWrapperService>();
        builder.Services.AddScoped<IWebhookService, WebhookService>();

        return builder;
    }
    
    public static WebApplicationBuilder AddLibApiAuthentication(this WebApplicationBuilder builder)
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
        LibEndpoints.MapLibEndpoints(app, loggerFactory);
        AgentEndpoints.MapAgentEndpoints(app, loggerFactory);
        WebhookEndpoints.MapWebhookEndpoints(app);
        
        return app;
    }
} 