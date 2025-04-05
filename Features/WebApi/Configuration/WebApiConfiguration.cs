using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using XiansAi.Server.Endpoints;
using XiansAi.Server.Services.Web;

namespace XiansAi.Server.Features.WebApi.Configuration;

public static class WebApiConfiguration
{
    public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Register Web API specific services
        builder.Services.AddScoped<WorkflowStarterEndpoint>();
        builder.Services.AddScoped<WorkflowEventsEndpoint>();
        builder.Services.AddScoped<WorkflowFinderEndpoint>();
        builder.Services.AddScoped<WorkflowCancelEndpoint>();
        builder.Services.AddScoped<CertificateEndpoint>();
        builder.Services.AddScoped<InstructionsEndpoint>();
        builder.Services.AddScoped<DefinitionsEndpoint>();
        builder.Services.AddScoped<TenantEndpoint>();
        
        return builder;
    }
    
    public static WebApplication UseWebApiEndpoints(this WebApplication app)
    {
        // Map Web API endpoints
        WebEndpointExtensions.MapWebEndpoints(app);
        PublicEndpointExtensions.MapPublicEndpoints(app);
        
        return app;
    }
} 