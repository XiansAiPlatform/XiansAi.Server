using DotNetEnv;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.AzureAppServices;
using XiansAi.Server.Features.Shared.Configuration;
using XiansAi.Server.Features.WebApi.Configuration;
using XiansAi.Server.Features.LibApi.Configuration;

namespace XiansAi.Server;

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
    /// <exception cref="InvalidOperationException">Thrown when configuration fails to load.</exception>
    public static void Main(string[] args)
    {
        try
        {
            // Load environment variables from .env file
            Env.Load();

            // Parse command line args to determine which services to run
            var serviceType = ParseServiceTypeFromArgs(args);
            
            // Create the builder and load service-specific configuration
            var builder = WebApplication.CreateBuilder(args);
            builder.LoadServiceConfiguration(serviceType);
            
            // Configure logging
            _logger = LoggerFactory.Create(logBuilder => logBuilder.AddConsole())
                .CreateLogger<Program>();
            
            _logger.LogInformation("Starting XiansAi.Server application");
            _logger.LogInformation($"Starting service with type: {serviceType}");

            // Configure shared services and configuration first
            builder.AddSharedServices();
            builder.AddSharedConfiguration();

            // Add Azure logging for cloud deployments
            builder.Logging.AddAzureWebAppDiagnostics();

            // Register microservice-specific services based on service type
            switch (serviceType)
            {
                case ServiceType.WebApi:
                    builder.AddWebApiServices();
                    break;
                
                case ServiceType.LibApi:
                    builder.AddLibApiServices();
                    break;
                
                case ServiceType.All:
                default:
                    builder.AddWebApiServices();
                    builder.AddLibApiServices();
                    break;
            }

            var app = builder.Build();

            // Configure shared middleware
            app.UseSharedMiddleware();

            // Configure microservice-specific endpoints
            switch (serviceType)
            {
                case ServiceType.WebApi:
                    app.UseWebApiEndpoints();
                    break;
                
                case ServiceType.LibApi:
                    app.UseLibApiEndpoints();
                    break;
                
                case ServiceType.All:
                default:
                    app.UseWebApiEndpoints();
                    app.UseLibApiEndpoints();
                    break;
            }

            // Map controllers (shared between services)
            app.MapControllers();

            // Verify critical configuration
            ValidateConfiguration(app.Configuration);
            
            // Run the app
            _logger.LogInformation("Application configured successfully, starting server");
            app.Run();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Application startup failed");
            throw; // Re-throw to let the runtime handle the exception
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
