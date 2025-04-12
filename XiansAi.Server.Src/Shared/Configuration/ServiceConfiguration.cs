using Features.Shared.Configuration.OpenApi;

namespace Features.Shared.Configuration;

public static class ServiceConfiguration
{
    public static WebApplicationBuilder AddSharedServices(this WebApplicationBuilder builder)
    {
        // Add infrastructure services (clients, data access, etc.)
        builder.Services.AddInfrastructureServices(builder.Configuration);
        
        // Add common api-related services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddServerOpenApi();
        builder.Services.AddControllers();
        
        // Add health checks
        builder.Services.AddHealthChecks();
        
        // Register TimeProvider for DI
        builder.Services.AddSingleton(TimeProvider.System);
        
        // Add HttpContextAccessor for access to the current HttpContext
        builder.Services.AddHttpContextAccessor();
        
        return builder;
    }
} 