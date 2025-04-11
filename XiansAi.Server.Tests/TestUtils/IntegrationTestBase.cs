using Xunit;
using XiansAi.Server.Tests.TestUtils;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;
using System.Net.Http.Headers;

namespace XiansAi.Server.Tests.TestUtils;

public abstract class IntegrationTestBase : IClassFixture<MongoDbFixture>
{
    protected readonly MongoDbFixture _mongoFixture;
    protected XiansAiWebApplicationFactory _factory;
    protected HttpClient _client;
    protected IMongoDatabase _database;

    protected IntegrationTestBase(MongoDbFixture mongoFixture)
    {
        _mongoFixture = mongoFixture;
        
        // Initialize environment variables for certificate authentication
        // This should be done before creating the factory to ensure proper configuration
        TestCertificateHelper.Initialize();
        
        _factory = new XiansAiWebApplicationFactory(mongoFixture);
        
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // Configure client with certificate
        ConfigureClientWithCertificate();
        
        _database = _mongoFixture.Database;
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }
    
    private void ConfigureClientWithCertificate()
    {
        try
        {
            if (_client == null) 
            {
                throw new InvalidOperationException("Client is not initialized");
            }
            
            var apiKey = TestCertificateHelper.LoadApiKeyFromEnv();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("API key is null or empty");
            }
            
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to configure client with certificate: {ex.Message}", ex);
        }
    }
} 