using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using XiansAi.Server.Endpoints;
using XiansAi.Server.Services.Lib;

namespace XiansAi.Server.Features.LibApi.Configuration;

public static class LibApiConfiguration
{
    public static WebApplicationBuilder AddLibApiServices(this WebApplicationBuilder builder)
    {
        // Register Lib API specific services
        builder.Services.AddScoped<InstructionsServerEndpoint>();
        builder.Services.AddScoped<ActivitiesServerEndpoint>();
        builder.Services.AddScoped<DefinitionsServerEndpoint>();
        builder.Services.AddScoped<WorkflowSignalEndpoint>();
        builder.Services.AddScoped<ObjectCacheEndpoint>();

        return builder;
    }
    
    public static WebApplication UseLibApiEndpoints(this WebApplication app)
    {
        // Map Lib API endpoints
        LibEndpointExtensions.MapLibEndpoints(app);
        AgentEndpointExtensions.MapAgentEndpoints(app);
        
        return app;
    }
} 