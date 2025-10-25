using Features.PublicApi.Endpoints;
using Features.PublicApi.Services;

namespace Features.PublicApi.Configuration;

public static class PublicApiConfiguration
{
    public static WebApplicationBuilder AddPublicApiServices(this WebApplicationBuilder builder)
    {
        // Register Public API specific services (no authentication services needed)
        builder.Services.AddScoped<IPublicRegistrationService, PublicRegistrationService>();
        
        // Note: Rate limiting is now configured centrally in SharedConfiguration.AddRateLimiting()
        // This provides consistent rate limiting across all APIs with configurable policies
        
        return builder;
    }
    
    public static WebApplication UsePublicApiEndpoints(this WebApplication app)
    {
        // Map Public API endpoints (no authentication required, but rate limited)
        // Rate limiting middleware is applied centrally in SharedConfiguration.UseSharedMiddleware()
        RegisterEndpoints.MapRegisterEndpoints(app);
        GitHubAuthEndpoints.MapGitHubAuthEndpoints(app);
        
        return app;
    }
}
