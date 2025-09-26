using System.Security.Cryptography.X509Certificates;
using DotNetEnv;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Tests.TestUtils;

public static class TestCertificateHelper
{
    private static readonly object _lock = new object();
    private static bool _isInitialized;
    private static string? _apiKey;
    private static string? _rootCertificate;
    private static string? _rootPrivateKey;
    private static X509Certificate2? _testCertificate;
    
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
                    // Create a default .env file if it doesn't exist
                    File.WriteAllText(fullEnvPath, "APP_SERVER_API_KEY=test-api-key");
                }
                
                // Load environment variables
                Env.Load(fullEnvPath);
                
                // Cache the API key
                _apiKey = Environment.GetEnvironmentVariable("APP_SERVER_API_KEY");
                if (string.IsNullOrEmpty(_apiKey))
                {
                    _apiKey = "test-api-key";
                    Environment.SetEnvironmentVariable("APP_SERVER_API_KEY", _apiKey);
                }

                // Generate test certificates if not already set
                if (_testCertificate == null)
                {
                    GenerateTestCertificates();
                }
                
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                // Reset initialization state on error
                _isInitialized = false;
                _apiKey = null;
                _rootCertificate = null;
                _rootPrivateKey = null;
                _testCertificate = null;
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

    public static string GetTestRootCertificate()
    {
        // Ensure initialization before accessing the certificate
        if (!_isInitialized)
        {
            Initialize();
        }
        
        return _rootCertificate ?? Convert.ToBase64String(_testCertificate!.RawData);
    }

    public static string GetTestRootPrivateKey()
    {
        // Ensure initialization before accessing the private key
        if (!_isInitialized)
        {
            Initialize();
        }
        
        return _rootPrivateKey ?? Convert.ToBase64String(_testCertificate!.GetRSAPrivateKey()!.ExportRSAPrivateKey());
    }

    private static void GenerateTestCertificates()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=Test Root CA",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add basic constraints
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));

        // Add key usage
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign |
                X509KeyUsageFlags.CrlSign |
                X509KeyUsageFlags.DigitalSignature,
                true));

        // Create self-signed certificate
        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        // Store the certificate and private key
        _testCertificate = cert;
        _rootCertificate = Convert.ToBase64String(cert.RawData);
        _rootPrivateKey = Convert.ToBase64String(cert.GetRSAPrivateKey()!.ExportRSAPrivateKey());
    }
} 