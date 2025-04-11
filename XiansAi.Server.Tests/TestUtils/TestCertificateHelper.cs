using System.Security.Cryptography.X509Certificates;
using DotNetEnv;
using System.IO;

namespace XiansAi.Server.Tests.TestUtils;

public static class TestCertificateHelper
{
    private static readonly object _lock = new object();
    private static bool _isInitialized;
    private static string? _apiKey;
    
    public static void Initialize(string envPath = ".env")
    {
        // Double-check locking pattern for thread safety
        if (_isInitialized) return;
        
        lock (_lock)
        {
            if (_isInitialized) return;
            
            try
            {
                // Get an absolute path to the .env file in the test project root
                string testProjectDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../"));
                string fullEnvPath = Path.Combine(testProjectDirectory, envPath);
                
                if (!File.Exists(fullEnvPath))
                {
                    throw new FileNotFoundException($"Environment file not found at: {fullEnvPath}");
                }
                
                // Load environment variables
                Env.Load(fullEnvPath);
                
                // Cache the API key
                _apiKey = Environment.GetEnvironmentVariable("APP_SERVER_API_KEY");
                if (string.IsNullOrEmpty(_apiKey))
                {
                    throw new InvalidOperationException("APP_SERVER_API_KEY environment variable not found. Make sure to set it in the .env file.");
                }
                
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                // Reset initialization state on error
                _isInitialized = false;
                _apiKey = null;
                throw new InvalidOperationException($"Failed to initialize TestCertificateHelper: {ex.Message}", ex);
            }
        }
    }
    
    public static string LoadApiKeyFromEnv()
    {
        // Ensure initialization before accessing the API key
        if (!_isInitialized)
        {
            Initialize();
        }
        
        // Double-check that we have a valid API key
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("API key is null or empty after initialization.");
        }
        
        return _apiKey;
    }
} 