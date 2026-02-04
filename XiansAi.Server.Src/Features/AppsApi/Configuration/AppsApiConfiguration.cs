using Features.AppsApi.Endpoints;
using Features.AppsApi.Repositories;
using Features.AppsApi.Services;
using Features.AppsApi.Handlers;

namespace Features.AppsApi.Configuration;

/// <summary>
/// Configuration for AppsApi - app integrations for external platforms (Slack, Teams, etc.)
/// </summary>
public static class AppsApiConfiguration
{
    /// <summary>
    /// Adds AppsApi services to the service collection.
    /// This registers repositories and services needed for app integrations.
    /// </summary>
    public static WebApplicationBuilder AddAppsApiServices(this WebApplicationBuilder builder)
    {
        // Register AppsApi repositories
        builder.Services.AddScoped<IAppIntegrationRepository, AppIntegrationRepository>();
        
        // Register AppsApi services
        builder.Services.AddScoped<IAppIntegrationService, AppIntegrationService>();
        
        // Register platform-specific webhook handlers
        builder.Services.AddScoped<ISlackWebhookHandler, SlackWebhookHandler>();
        
        // Register background service for routing outbound messages
        builder.Services.AddHostedService<AppMessageRouterService>();
        
        // Register HttpClient for outbound API calls
        builder.Services.AddHttpClient("SlackApi", client =>
        {
            client.BaseAddress = new Uri("https://slack.com/api/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        
        builder.Services.AddHttpClient("TeamsApi", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        
        return builder;
    }

    /// <summary>
    /// Maps AppsApi public webhook endpoints to the application.
    /// These endpoints are called by external platforms (Slack, Teams, etc.)
    /// and use platform-specific authentication (signature verification).
    /// </summary>
    public static WebApplication UseAppsApiEndpoints(this WebApplication app)
    {
        // Map public webhook endpoints
        app.MapAppWebhookEndpoints();
        
        return app;
    }
}
