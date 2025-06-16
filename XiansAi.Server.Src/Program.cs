using DotNetEnv;
using Features.AgentApi.Configuration;
using Features.Shared.Configuration;
using Features.WebApi.Configuration;
using Shared.Auth;
using Features.WebApi.Auth;
using Shared.Data;

/// <summary>
/// Entry point class for the XiansAi.Server application.
/// </summary>
public class Program
{
    private static ILogger _logger = null!;
    
    // Service types that can be enabled/disabled
    public enum ServiceType
    {
        WebApi,
        LibApi,
        All
    }
    
    /// <summary>
    /// Application entry point. Sets up the web application with services and middleware.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static async Task Main(string[] args)
    {
        try
        {
            // Load environment variables from the correct file based on environment
            Env.Load();
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            var envFile = envName == "Production" ? ".env.production" : ".env.development";
            Env.Load(envFile);

            // Parse command line args to determine which services to run
            var serviceType = ParseServiceTypeFromArgs(args);
            var loggerFactory = LoggerFactory.Create(logBuilder => logBuilder.AddConsole());
            // Initialize logger
            _logger = loggerFactory.CreateLogger<Program>();
            _logger.LogInformation($"Loading environment variables from {envFile} (ASPNETCORE_ENVIRONMENT={envName})");
            
            // Build and run the application
            var builder = CreateApplicationBuilder(args, serviceType, loggerFactory);

            builder.LoadServiceConfiguration(serviceType); // best place!

            var app = await ConfigureApplication(builder, serviceType, loggerFactory);
            
            // Run the app
            _logger.LogInformation("Application configured successfully, starting server");
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Application startup failed");
            throw; // Re-throw to let the runtime handle the exception
        }
    }

    /// <summary>
    /// Creates and configures the WebApplicationBuilder with appropriate services.
    /// </summary>
    private static WebApplicationBuilder CreateApplicationBuilder(string[] args, ServiceType serviceType, ILoggerFactory loggerFactory )
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.LoadServiceConfiguration(serviceType);
        
        // Configure shared services and configuration first
        SharedConfiguration.AddSharedServices(builder);
        
        // Add Azure logging for cloud deployments
        builder.Logging.AddAzureWebAppDiagnostics();

        // Register microservice-specific services based on service type
        ConfigureServicesByType(builder, serviceType, loggerFactory);
        
        // Register the TenantContext
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        // Register the MongoDbContext
        builder.Services.AddScoped<IMongoDbContext, MongoDbContext>();
        
        return builder;
    }
    
    /// <summary>
    /// Configures services based on the selected service type.
    /// </summary>
    private static void ConfigureServicesByType(WebApplicationBuilder builder, ServiceType serviceType, ILoggerFactory loggerFactory)
    {
        switch (serviceType)
        {
            case ServiceType.WebApi:
                builder.AddWebApiServices();
                builder.AddWebApiAuth();
                break;
            
            case ServiceType.LibApi:
                builder.AddAgentApiServices();
                builder.AddAgentApiAuth();
                break;
            
            case ServiceType.All:
            default:
                builder.AddWebApiServices();
                builder.AddAgentApiServices();
                builder.AddAgentApiAuth();
                builder.AddWebApiAuth();
                break;
        }
    }

    /// <summary>
    /// Configures the web application with appropriate middleware and endpoints.
    /// </summary>
    private static async Task<WebApplication> ConfigureApplication(WebApplicationBuilder builder, ServiceType serviceType, ILoggerFactory loggerFactory)
    {
        var app = builder.Build();

        // Call your extension method here, passing the ServiceType
        builder.LoadServiceConfiguration(serviceType);
        
        
        // Configure shared middleware
        app.UseSharedMiddleware();

        // Configure service-specific endpoints and middleware
        ConfigureEndpointsByType(app, serviceType, loggerFactory);

        // Map controllers (shared between services)
        app.MapControllers();

        // Verify critical configuration
        ValidateConfiguration(app.Configuration);
        
        // Synchronize MongoDB indexes & seed default data
        using var scope = app.Services.CreateScope();
        var indexSynchronizer = scope.ServiceProvider.GetRequiredService<IMongoIndexSynchronizer>();
        await indexSynchronizer.EnsureIndexesAsync();

        // Seed default data 
        await SeedData.SeedDefaultDataAsync(app.Services, _logger);

        return app;
    }
    
    /// <summary>
    /// Configures endpoints and middleware based on the selected service type.
    /// </summary>
    private static void ConfigureEndpointsByType(WebApplication app, ServiceType serviceType, ILoggerFactory loggerFactory)
    {
        switch (serviceType)
        {
            case ServiceType.WebApi:
                app.UseWebApiEndpoints();
                break;
            
            case ServiceType.LibApi:
                app.UseAgentApiEndpoints(loggerFactory);
                break;
            
            case ServiceType.All:
            default:
                app.UseWebApiEndpoints();
                app.UseAgentApiEndpoints(loggerFactory);
                break;
        }
    }

    /// <summary>
    /// Parses command line arguments to determine which service type to run.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The service type to run.</returns>
    private static ServiceType ParseServiceTypeFromArgs(string[] args)
    {
        // Default to running all services if no arguments are provided
        if (args == null || args.Length == 0)
        {
            return ServiceType.All;
        }

        foreach (var arg in args)
        {
            if (arg.Equals("--web", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceType.WebApi;
            }
            else if (arg.Equals("--lib", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceType.LibApi;
            }
            else if (arg.Equals("--all", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceType.All;
            }
        }

        // Default to running all services if arguments are not recognized
        return ServiceType.All;
    }

    /// <summary>
    /// Validates that critical configuration values are present.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <exception cref="InvalidOperationException">Thrown when required configuration is missing.</exception>
    private static void ValidateConfiguration(IConfiguration config)
    {
        if (config == null)
        {
            _logger.LogError("Configuration is null");
            throw new InvalidOperationException("Configuration is null");
        }

        var mongoConnString = config["MongoDB:ConnectionString"];
        if (string.IsNullOrWhiteSpace(mongoConnString))
        {
            _logger.LogWarning("MongoDB connection string is not configured");
        }
        
        _logger.LogInformation("Configuration loaded successfully");
    }
}
