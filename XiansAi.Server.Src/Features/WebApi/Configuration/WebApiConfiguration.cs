using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Features.WebApi.Endpoints;
using Features.WebApi.Services.Web;
using Features.WebApi.Auth;
using Features.WebApi.Repositories;

namespace Features.WebApi.Configuration;

public static class WebApiConfiguration
{
    public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Register Web API specific services
        builder.Services.AddSingleton<IAuth0MgtAPIConnect, Auth0MgtAPIConnect>();
        builder.Services.AddScoped<WorkflowStarterEndpoint>();
        builder.Services.AddScoped<WorkflowEventsEndpoint>();
        builder.Services.AddScoped<WorkflowFinderEndpoint>();
        builder.Services.AddScoped<WorkflowCancelEndpoint>();
        builder.Services.AddScoped<CertificateEndpoint>();
        builder.Services.AddScoped<InstructionsEndpoint>();
        builder.Services.AddScoped<DefinitionsEndpoint>();
        builder.Services.AddScoped<TenantEndpoint>();
        
        // Register Webhook services
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>(sp => 
        {
            var dbService = sp.GetRequiredService<IDatabaseService>();
            return new WebhookRepository(dbService.GetDatabase().GetAwaiter().GetResult());
        });
        builder.Services.AddScoped<WebhookEndpoint>();
        
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