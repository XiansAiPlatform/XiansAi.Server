using DotNetEnv;
using Features.AgentApi.Configuration;
using Features.Shared.Configuration;
using Features.WebApi.Configuration;
using Shared.Auth;
using Features.WebApi.Auth;
using Shared.Data;
using XiansAi.Server.Features.WebApi.Scripts;
using Features.UserApi.Configuration;

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
        UserApi,
        All
    }

    /// <summary>
    /// Represents the parsed command line arguments.
    /// </summary>
    public class CommandLineArgs
    {
        public ServiceType ServiceType { get; set; } = ServiceType.All;
        public string? CustomEnvFile { get; set; }
        public bool ShowHelp { get; set; } = false;
    }
    
    /// <summary>
    /// Application entry point. Sets up the web application with services and middleware.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static async Task Main(string[] args)
    {
        try
        {
            // Parse command line arguments
            var commandLineArgs = ParseCommandLineArgs(args);
            
            // Show help if requested
            if (commandLineArgs.ShowHelp)
            {
                ShowUsageInformation();
                return;
            }
            
            // Load environment variables
            LoadEnvironmentVariables(commandLineArgs.CustomEnvFile);
            
            var loggerFactory = LoggerFactory.Create(logBuilder => logBuilder.AddConsole());
            // Initialize logger
            _logger = loggerFactory.CreateLogger<Program>();
            
            // Log startup information
            LogStartupInformation(commandLineArgs);
            
            // Build and run the application
            var builder = CreateApplicationBuilder(args, commandLineArgs.ServiceType, loggerFactory);
            var app = await ConfigureApplication(builder, commandLineArgs.ServiceType, loggerFactory);
            
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
    /// Loads environment variables from the appropriate file(s).
    /// </summary>
    /// <param name="customEnvFile">Optional custom environment file path specified via command line.</param>
    private static void LoadEnvironmentVariables(string? customEnvFile)
    {
        if (!string.IsNullOrWhiteSpace(customEnvFile))
        {
            // Load custom environment file if specified
            if (File.Exists(customEnvFile))
            {
                Console.WriteLine($"Loading custom environment file: {customEnvFile}");
                Env.Load(customEnvFile);
            }
            else
            {
                throw new FileNotFoundException($"Custom environment file not found: {customEnvFile}");
            }
        }
        else
        {
            // Always load the default .env file first (if it exists)
            try
            {
                Env.Load();
            }
            catch (FileNotFoundException)
            {
                // Default .env file doesn't exist, which is fine
            }
            
            // Fall back to environment-based file selection
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            var envFile = envName == "Production" ? ".env.production" : ".env.development";
            
            try
            {
                Console.WriteLine($"Loading environment variables from {envFile} (ASPNETCORE_ENVIRONMENT={envName})");
                Env.Load(envFile);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"Environment file {envFile} not found, continuing with existing environment variables");
            }
        }
    }

    /// <summary>
    /// Creates and configures the WebApplicationBuilder with appropriate services.
    /// </summary>
    private static WebApplicationBuilder CreateApplicationBuilder(string[] args, ServiceType serviceType, ILoggerFactory loggerFactory )
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Configure shared services and configuration first
        SharedConfiguration.AddSharedServices(builder);
        
        // Add Azure logging for cloud deployments
        builder.Logging.AddAzureWebAppDiagnostics();

        // Register microservice-specific services based on service type
        ConfigureServicesByType(builder, serviceType, loggerFactory);
        
        // Register the TenantContext
        // builder.Services.AddScoped<ITenantContext, TenantContext>();
        // MongoDbContext is registered as singleton in SharedServices.cs
        
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
            case ServiceType.UserApi:
                builder.AddUserApiServices();
                builder.AddUserApiAuth();
                break;

            case ServiceType.All:
            default:
                builder.AddWebApiServices();
                builder.AddAgentApiServices();
                builder.AddAgentApiAuth();
                builder.AddWebApiAuth();
                builder.AddUserApiServices();
                builder.AddUserApiAuth();
                break;
        }
    }

    /// <summary>
    /// Configures the web application with appropriate middleware and endpoints.
    /// </summary>
    private static async Task<WebApplication> ConfigureApplication(WebApplicationBuilder builder, ServiceType serviceType, ILoggerFactory loggerFactory)
    {
        var app = builder.Build();

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
        // Map endpoints based on service type
        switch (serviceType)
        {
            case ServiceType.WebApi:
                app.UseWebApiEndpoints();
                break;
            
            case ServiceType.LibApi:
                app.UseAgentApiEndpoints(loggerFactory);
                break;
            case ServiceType.UserApi:
                app.UseUserApiEndpoints();
                break;
            case ServiceType.All:
            default:
                app.UseWebApiEndpoints();
                app.UseAgentApiEndpoints(loggerFactory);
                app.UseUserApiEndpoints();
                break;
        }
    }

    /// <summary>
    /// Parses command line arguments to determine which service type to run and optional environment file.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The parsed command line arguments.</returns>
    private static CommandLineArgs ParseCommandLineArgs(string[] args)
    {
        var commandLineArgs = new CommandLineArgs();

        // Default to running all services if no arguments are provided
        if (args == null || args.Length == 0)
        {
            return commandLineArgs;
        }

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--web", StringComparison.OrdinalIgnoreCase))
            {
                commandLineArgs.ServiceType = ServiceType.WebApi;
            }
            else if (arg.Equals("--lib", StringComparison.OrdinalIgnoreCase))
            {
                commandLineArgs.ServiceType = ServiceType.LibApi;
            }
            else if (arg.Equals("--user", StringComparison.OrdinalIgnoreCase))
            {
                commandLineArgs.ServiceType = ServiceType.UserApi;
            }
            else if (arg.Equals("--all", StringComparison.OrdinalIgnoreCase))
            {
                commandLineArgs.ServiceType = ServiceType.All;
            }
            else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || 
                     arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                commandLineArgs.ShowHelp = true;
            }
            else if (arg.StartsWith("--env-file=", StringComparison.OrdinalIgnoreCase))
            {
                // Handle --env-file=filepath format
                commandLineArgs.CustomEnvFile = arg.Substring("--env-file=".Length);
            }
            else if (arg.Equals("--env-file", StringComparison.OrdinalIgnoreCase))
            {
                // Handle --env-file filepath format (separate arguments)
                if (i + 1 < args.Length)
                {
                    commandLineArgs.CustomEnvFile = args[i + 1];
                    i++; // Skip the next argument since we've processed it
                }
                else
                {
                    throw new ArgumentException("--env-file argument requires a file path");
                }
            }
            else if (arg.StartsWith("--environment=", StringComparison.OrdinalIgnoreCase))
            {
                // Handle environment setting - this is used by test framework
                var env = arg.Substring("--environment=".Length);
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", env);
            }
            else if (arg.StartsWith("--contentRoot=", StringComparison.OrdinalIgnoreCase))
            {
                // Handle content root setting - this is used by test framework
                var contentRoot = arg.Substring("--contentRoot=".Length);
                Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", contentRoot);
            }
            else if (arg.StartsWith("--applicationName=", StringComparison.OrdinalIgnoreCase))
            {
                // Handle application name setting - this is used by test framework
                var appName = arg.Substring("--applicationName=".Length);
                Environment.SetEnvironmentVariable("ASPNETCORE_APPLICATIONNAME", appName);
            }
            else if (arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            {
                // Unknown argument starting with -- 
                // Ignore unknown arguments in test environment
                if (!Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?.Contains("Test", StringComparison.OrdinalIgnoreCase) ?? true)
                {
                    throw new ArgumentException($"Unknown argument: {arg}. Use --help to see available options.");
                }
            }
        }

        return commandLineArgs;
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

        var configName = config["CONFIG_NAME"];
        if (string.IsNullOrWhiteSpace(configName))
        {
            _logger.LogWarning("CONFIG_NAME is not configured");
        }
        
        _logger.LogInformation("Configuration loaded successfully for {ConfigName}", configName);
    }

    /// <summary>
    /// Displays usage information for the application.
    /// </summary>
    private static void ShowUsageInformation()
    {
        Console.WriteLine("XiansAi.Server - Multi-service application");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run [options]");
        Console.WriteLine();
        Console.WriteLine("Service Options:");
        Console.WriteLine("  --web                 Start WebApi service only");
        Console.WriteLine("  --lib                 Start LibApi (Agent API) service only");
        Console.WriteLine("  --user                Start UserApi service only");
        Console.WriteLine("  --all                 Start all services (default)");
        Console.WriteLine();
        Console.WriteLine("Environment Options:");
        Console.WriteLine("  --env-file=<path>     Use custom environment file");
        Console.WriteLine("  --env-file <path>     Use custom environment file (alternative syntax)");
        Console.WriteLine();
        Console.WriteLine("Other Options:");
        Console.WriteLine("  --help, -h            Show this help information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run                              # Start all services with default env");
        Console.WriteLine("  dotnet run --web                        # Start WebApi service only");
        Console.WriteLine("  dotnet run --lib --env-file=.env.local  # Start LibApi with custom env file");
        Console.WriteLine("  dotnet run --user --env-file .env.test  # Start UserApi with test environment");
        Console.WriteLine();
        Console.WriteLine("Environment File Loading:");
        Console.WriteLine("  1. Custom file (if specified with --env-file)");
        Console.WriteLine("  2. Default .env file (if exists)");
        Console.WriteLine("  3. Environment-specific file (.env.development or .env.production)");
        Console.WriteLine();
        Console.WriteLine("Services:");
        Console.WriteLine("  WebApi    - Main web API with agent management, workflows, tenants");
        Console.WriteLine("  LibApi    - Agent API for library/agent interactions");
        Console.WriteLine("  UserApi   - User-facing API with webhooks and websockets");
    }

    /// <summary>
    /// Logs startup information about the selected service type and environment configuration.
    /// </summary>
    /// <param name="commandLineArgs">The parsed command line arguments.</param>
    private static void LogStartupInformation(CommandLineArgs commandLineArgs)
    {
        var serviceDescription = commandLineArgs.ServiceType switch
        {
            ServiceType.WebApi => "WebApi service (Main web API)",
            ServiceType.LibApi => "LibApi service (Agent API)",
            ServiceType.UserApi => "UserApi service (User-facing API)",
            ServiceType.All => "All services (WebApi, LibApi, UserApi)",
            _ => "Unknown service type"
        };

        _logger.LogInformation("Starting XiansAi.Server with {ServiceType}: {ServiceDescription}", 
            commandLineArgs.ServiceType, serviceDescription);

        if (!string.IsNullOrWhiteSpace(commandLineArgs.CustomEnvFile))
        {
            _logger.LogInformation("Using custom environment file: {EnvFile}", commandLineArgs.CustomEnvFile);
        }
        else
        {
            var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            _logger.LogInformation("Using default environment file loading for environment: {Environment}", envName);
        }
    }
}