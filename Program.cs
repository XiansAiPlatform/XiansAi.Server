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

        if (builder.Environment.IsProduction() || args.Contains("--use-keyvault")) {
            // Add Key Vault configuration
            string keyVaultUrl = "https://kv-xiansai.vault.azure.net/";
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUrl),
                new DefaultAzureCredential());
        }


        // Configure all services
        builder.ConfigureServices();

        var app = builder.Build();

        // Configure middleware pipeline
        app.ConfigureMiddleware();
        app.Run();
    }
}
