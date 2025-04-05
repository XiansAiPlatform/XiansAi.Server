using Microsoft.Extensions.Configuration;

namespace XiansAi.Server.Features.Shared.Configuration;

public static class ConfigurationLoader
{
    public static WebApplicationBuilder LoadServiceConfiguration(this WebApplicationBuilder builder, Program.ServiceType serviceType)
    {
        // Load base configuration
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        
        // Environment-specific configuration
        var environment = builder.Environment.EnvironmentName;
        builder.Configuration.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);
        
        // Service-specific configuration
        switch (serviceType)
        {
            case Program.ServiceType.WebApi:
                builder.Configuration.AddJsonFile("appsettings.WebApi.json", optional: true, reloadOnChange: true);
                builder.Configuration.AddJsonFile($"appsettings.WebApi.{environment}.json", optional: true, reloadOnChange: true);
                break;
                
            case Program.ServiceType.LibApi:
                builder.Configuration.AddJsonFile("appsettings.LibApi.json", optional: true, reloadOnChange: true);
                builder.Configuration.AddJsonFile($"appsettings.LibApi.{environment}.json", optional: true, reloadOnChange: true);
                break;
                
            case Program.ServiceType.All:
                // Load both configurations
                builder.Configuration.AddJsonFile("appsettings.WebApi.json", optional: true, reloadOnChange: true);
                builder.Configuration.AddJsonFile($"appsettings.WebApi.{environment}.json", optional: true, reloadOnChange: true);
                builder.Configuration.AddJsonFile("appsettings.LibApi.json", optional: true, reloadOnChange: true);
                builder.Configuration.AddJsonFile($"appsettings.LibApi.{environment}.json", optional: true, reloadOnChange: true);
                break;
        }
        
        // Add environment variables
        builder.Configuration.AddEnvironmentVariables();
        
        return builder;
    }
} 