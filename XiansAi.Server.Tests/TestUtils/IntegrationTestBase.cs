using Xunit;
using XiansAi.Server.Tests.TestUtils;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;
using System.Net.Http.Headers;
using System.Net;

namespace XiansAi.Server.Tests.TestUtils;

public abstract class IntegrationTestBase : IClassFixture<MongoDbFixture>
{
    protected readonly MongoDbFixture _mongoFixture;
    protected XiansAiWebApplicationFactory _factory;
    protected RetryHttpClient _client;
    protected IMongoDatabase _database;
    protected const string TestTenantId = "test-tenant";
    protected const string TestApiKey = "test-api-key";
    protected const string TestCertificateThumbprint = "test-certificate-thumbprint";

    protected IntegrationTestBase(MongoDbFixture mongoFixture, string? environment = null)
    {
        _mongoFixture = mongoFixture;
        
        // Initialize environment variables for certificate authentication
        // This should be done before creating the factory to ensure proper configuration
        TestCertificateHelper.Initialize();
        
        _factory = new XiansAiWebApplicationFactory(mongoFixture, environment);
        
        var httpClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("http://localhost")
        });
        
        // Configure client with authentication
        ConfigureClientWithAuth(httpClient);
        
        // Create retry client
        _client = new RetryHttpClient(httpClient, () => ConfigureClientWithAuth(httpClient));
        
        _database = _mongoFixture.Database;
    }

    public Task DisposeAsync()
    {
        _client?.DisposeAsync();
        return Task.CompletedTask;
    }
    
    protected virtual void ConfigureClientWithAuth(HttpClient client)
    {
        try
        {
            if (client == null) 
            {
                throw new InvalidOperationException("Client is not initialized");
            }
            
            // Clear existing headers
            client.DefaultRequestHeaders.Clear();
            
            // Add API key for API endpoints
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestApiKey);

            // Add test certificate header for agent endpoints
            client.DefaultRequestHeaders.Add("X-Test-Certificate", TestCertificateThumbprint);

            // Add tenant header
            client.DefaultRequestHeaders.Add("X-Tenant-Id", TestTenantId);

            // Add accept headers
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Add user-agent
            client.DefaultRequestHeaders.Add("User-Agent", "XiansAi.Server.Tests");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to configure client with authentication: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Executes an HTTP request with retry logic for unauthorized responses and server timeouts
    /// </summary>
    /// <param name="request">The HTTP request to execute</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 5)</param>
    /// <param name="retryDelayMs">Delay between retries in milliseconds (default: 1000)</param>
    /// <returns>The HTTP response</returns>
    protected async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        HttpRequestMessage request,
        int maxRetries = 5,
        int retryDelayMs = 1000)
    {
        HttpResponseMessage? response = null;
        int retryCount = 0;

        while (retryCount <= maxRetries)
        {
            // Add authentication headers to each request
            if (!request.Headers.Contains("X-Tenant-Id"))
            {
                request.Headers.Add("X-Tenant-Id", TestTenantId);
            }

            if (!request.Headers.Contains("X-Test-Certificate"))
            {
                request.Headers.Add("X-Test-Certificate", TestCertificateThumbprint);
            }

            if (request.Headers.Authorization == null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestApiKey);
            }

            response = await _client.SendAsync(request);
            
            // Check if response is not a retryable status code or we've reached max retries
            bool isTimeout = response.StatusCode == HttpStatusCode.RequestTimeout || 
                             response.StatusCode == HttpStatusCode.GatewayTimeout;
            bool shouldRetry = response.StatusCode == HttpStatusCode.Unauthorized || isTimeout;
            
            if (!shouldRetry || retryCount == maxRetries)
            {
                return response;
            }

            retryCount++;
            if (retryCount <= maxRetries)
            {
                await Task.Delay(retryDelayMs);
                // Reconfigure the client with authentication before retrying
                ConfigureClientWithAuth(_client.HttpClient);
            }
        }
        if (response == null)
        {
            throw new InvalidOperationException("Failed to execute request after retries");
        }

        return response;
    }
}

/// <summary>
/// Base class for integration tests that run with Production environment configuration
/// </summary>
public abstract class ProductionIntegrationTestBase : IntegrationTestBase
{
    protected ProductionIntegrationTestBase(MongoDbFixture mongoFixture) : base(mongoFixture, "Production")
    {
    }
}

/// <summary>
/// Base class for integration tests that run with Staging environment configuration
/// </summary>
public abstract class StagingIntegrationTestBase : IntegrationTestBase
{
    protected StagingIntegrationTestBase(MongoDbFixture mongoFixture) : base(mongoFixture, "Staging")
    {
    }
}

/// <summary>
/// Base class for integration tests that run with Development environment configuration
/// </summary>
public abstract class DevelopmentIntegrationTestBase : IntegrationTestBase
{
    protected DevelopmentIntegrationTestBase(MongoDbFixture mongoFixture) : base(mongoFixture, "Development")
    {
    }
} 