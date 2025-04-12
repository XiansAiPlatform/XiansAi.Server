using Features.Shared.Configuration.OpenApi;

namespace Features.Shared.Configuration;

public static class SharedConfiguration
{
    public static WebApplicationBuilder AddSharedServices(this WebApplicationBuilder builder)
    {
        // Add services using specialized configuration classes
        builder = builder
            .AddCorsConfiguration();
            
        // Add common services
        builder = ServiceConfiguration.AddSharedServices(builder);
        
        return builder;
    }
    
    public static WebApplication UseSharedMiddleware(this WebApplication app)
    {
        // Configure environment-specific middleware
        if (app.Environment.IsDevelopment())
        {
            // Development-only middleware here (if any)
        }
        
        // Configure middleware using specialized configuration classes
        app = app
            .UseSwaggerConfiguration()
            .UseExceptionHandlingConfiguration()
            .UseHealthChecks();
        
        // Configure standard middleware
        app.UseCors("AllowAll");
        app.UseAuthentication();
        app.UseAuthorization();
        
        return app;
    }
} 