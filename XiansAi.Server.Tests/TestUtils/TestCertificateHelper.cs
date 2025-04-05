using System.Security.Cryptography.X509Certificates;
using DotNetEnv;
using System.IO;

namespace XiansAi.Server.Tests.TestUtils;

public static class TestCertificateHelper
{
    private static string? _envPath;
    
    public static void Initialize(string envPath = ".env")
    {
        // Get an absolute path to the .env file in the test project root
        string testProjectDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../"));
        _envPath = Path.Combine(testProjectDirectory, envPath);
        
        if (!File.Exists(_envPath))
        {
            throw new FileNotFoundException($"Environment file not found at: {_envPath}");
        }
        
        Env.Load(_envPath);
    }
    
    public static string LoadApiKeyFromEnv()
    {
        var apiKey = Environment.GetEnvironmentVariable("APP_SERVER_API_KEY");
        
        // For testing purposes, provide a default certificate if one is not found
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("APP_SERVER_API_KEY environment variable not found. Make sure to set it in the .env file.");
        }
        
        return apiKey;
    }
} 