using Xunit;
using XiansAi.Server.Tests.TestUtils;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;
using System.Net.Http.Headers;

namespace XiansAi.Server.Tests.TestUtils;

public abstract class IntegrationTestBase : IClassFixture<MongoDbFixture>
{
    protected readonly MongoDbFixture _mongoFixture;
    protected readonly XiansAiWebApplicationFactory _factory;
    protected readonly HttpClient _client;
    protected readonly IMongoDatabase _database;

    protected IntegrationTestBase(MongoDbFixture mongoFixture)
    {
        _mongoFixture = mongoFixture;
        
        // Initialize environment variables for certificate authentication
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
    
    private void ConfigureClientWithCertificate()
    {
        try
        {
            var apiKey = TestCertificateHelper.LoadApiKeyFromEnv();
            _client.DefaultRequestHeaders.Add("X-Client-Cert", apiKey);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to configure client with certificate: {ex.Message}", ex);
        }
    }
} 