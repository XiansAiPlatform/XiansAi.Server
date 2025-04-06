using Features.AgentApi.Endpoints;
using Features.AgentApi.Services.Agent;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Auth;
using Features.AgentApi.Repositories;
using XiansAi.Server.Utils;

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

        // Register Lib API specific services
        builder.Services.AddScoped<KnowledgeService>();
        builder.Services.AddScoped<ActivityHistoryService>();
        builder.Services.AddScoped<DefinitionsService>();
        builder.Services.AddScoped<WorkflowSignalService>();
        builder.Services.AddScoped<ObjectCacheWrapperService>();

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
    
    public static WebApplication UseLibApiEndpoints(this WebApplication app)
    {
        // Map Lib API endpoints
        LibEndpoints.MapLibEndpoints(app);
        AgentEndpoints.MapAgentEndpoints(app);
        
        return app;
    }
    
    // Since we're now using authentication handlers, we don't need the middleware anymore
    // But keeping the method to maintain backwards compatibility with Program.cs
    public static WebApplication UseLibApiMiddleware(this WebApplication app)
    {
        // No longer applying certificate validation middleware as it's now handled by the authentication handler
        return app;
    }
} 