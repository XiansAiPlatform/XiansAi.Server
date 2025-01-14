using DotNetEnv;
using Azure.Identity;

namespace XiansAi.Server;


public class Program
{
    private static ILogger _logger = null!;
    public static void Main(string[] args)
    {
        Env.Load();

        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Program>();
        var builder = WebApplication.CreateBuilder(args);

        // Configure all services
        builder.ConfigureServices();

        builder.Logging.AddAzureWebAppDiagnostics();

        var app = builder.Build();

        // Configure middleware pipeline
        app.ConfigureMiddleware();

        // Get configuration for testing
        var config = app.Configuration;
        if (config == null)
        {
            _logger.LogError("Configuration is null");
            throw new InvalidOperationException("Configuration is null");
        }
        var mongoConnString = config["MongoDB:ConnectionString"];
        Console.WriteLine($"MongoDB Connection String resolved: {mongoConnString}");
        
        _logger.LogInformation("Configuration loaded successfully");
        
        // run the app
        app.Run();
    }
}
