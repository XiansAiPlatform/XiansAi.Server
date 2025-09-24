using System.Net.Http.Json;
using System.Text.Json;
using Tests.TestUtils;
using Features.AgentApi.Endpoints;
using Features.AgentApi.Endpoints.Models;

namespace Tests.IntegrationTests.AgentApi;

/// <summary>
/// Cache endpoint tests that run with Production environment configuration
/// </summary>
public class CacheEndpointProductionTests : ProductionIntegrationTestBase, IClassFixture<MongoDbFixture>
{
    /*
    dotnet test --filter "FullyQualifiedName~CacheEndpointProductionTests"    
    */
    public CacheEndpointProductionTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetCacheValue_WhenKeyNotFound_ReturnsNoContent_WithProductionConfig()
    {
        // Arrange
        var request = new CacheKeyRequest { Key = "non-existent-key-prod" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/cache/get", request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.CacheEndpointProductionTests.SetAndGetCacheValue_ReturnsExpectedResult_WithProductionConfig"
    */
    [Fact]
    public async Task SetAndGetCacheValue_ReturnsExpectedResult_WithProductionConfig()
    {
        // Arrange
        string testKey = "test-key-production";
        var testValue = JsonDocument.Parse("{\"test\": \"value\", \"environment\": \"production\"}").RootElement;
        
        // Act - Set cache value
        var setRequest = new CacheSetRequest
        {
            Key = testKey,
            Value = testValue
        };
        var setResponse = await _client.PostAsJsonAsync("/api/agent/cache/set", setRequest);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getRequest = new CacheKeyRequest { Key = testKey };
        var getResponse = await _client.PostAsJsonAsync("/api/agent/cache/get", getRequest);
        
        // Assert - Get cache value
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("value", content.GetProperty("test").GetString());
        Assert.Equal("production", content.GetProperty("environment").GetString());
    }

    [Fact]
    public async Task SetCacheValue_WithRelativeExpiration_SetsValueCorrectly_WithProductionConfig()
    {
        // Arrange
        string testKey = "expiration-test-key-prod";
        var testValue = JsonDocument.Parse("{\"test\": \"expiration\", \"env\": \"prod\"}").RootElement;
        int relativeExpirationMinutes = 60; // 1 hour

        // Act - Set cache value with expiration
        var setRequest = new CacheSetRequest
        {
            Key = testKey,
            Value = testValue,
            RelativeExpirationMinutes = relativeExpirationMinutes
        };
        var setResponse = await _client.PostAsJsonAsync("/api/agent/cache/set", setRequest);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getRequest = new CacheKeyRequest { Key = testKey };
        var getResponse = await _client.PostAsJsonAsync("/api/agent/cache/get", getRequest);
        
        // Assert - Get cache value
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("expiration", content.GetProperty("test").GetString());
        Assert.Equal("prod", content.GetProperty("env").GetString());
    }
} 