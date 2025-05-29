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

    protected IntegrationTestBase(MongoDbFixture mongoFixture, string? environment = null)
    {
        _mongoFixture = mongoFixture;
        
        // Initialize environment variables for certificate authentication
        // This should be done before creating the factory to ensure proper configuration
        TestCertificateHelper.Initialize();
        
        _factory = new XiansAiWebApplicationFactory(mongoFixture, environment);
        
        var httpClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // Configure client with certificate
        ConfigureClientWithCertificate(httpClient);
        
        // Create retry client
        _client = new RetryHttpClient(httpClient, () => ConfigureClientWithCertificate(httpClient));
        
        _database = _mongoFixture.Database;
    }

    public Task DisposeAsync()
    {
        _client?.DisposeAsync();
        return Task.CompletedTask;
    }
    
    private void ConfigureClientWithCertificate(HttpClient client)
    {
        try
        {
            if (client == null) 
            {
                throw new InvalidOperationException("Client is not initialized");
            }
            
            var apiKey = TestCertificateHelper.LoadApiKeyFromEnv();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("API key is null or empty");
            }
            
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to configure client with certificate: {ex.Message}", ex);
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
                // Reconfigure the client with certificate before retrying
                ConfigureClientWithCertificate(_client.HttpClient);
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