using DotNetEnv;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.AzureAppServices;

namespace XiansAi.Server;

/// <summary>
/// Entry point class for the XiansAi.Server application.
/// </summary>
public class Program
{
    private static ILogger _logger = null!;
    
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

            // Configure logging
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Program>();
            _logger.LogInformation("Starting XiansAi.Server application");
            
            var builder = WebApplication.CreateBuilder(args);

            // Configure all services
            builder.ConfigureServices();

            // Add Azure logging for cloud deployments
            builder.Logging.AddAzureWebAppDiagnostics();

            var app = builder.Build();

            // Configure middleware pipeline
            app.ConfigureMiddleware();

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
