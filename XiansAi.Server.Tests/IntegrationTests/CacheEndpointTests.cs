using System.Net.Http.Json;
using System.Text.Json;
using XiansAi.Server.Tests.TestUtils;
using Features.AgentApi.Endpoints;

namespace XiansAi.Server.Tests.IntegrationTests;

public class CacheEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public CacheEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetCacheValue_WhenKeyNotFound_ReturnsNoContent()
    {
        // Arrange
        var request = new CacheKeyRequest { Key = "non-existent-key" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/client/cache/get", request);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetAndGetCacheValue_ReturnsExpectedResult()
    {
        // Arrange
        string testKey = "test-key";
        var testValue = JsonDocument.Parse("{\"test\": \"value\"}").RootElement;
        
        // Act - Set cache value
        var setRequest = new CacheSetRequest
        {
            Key = testKey,
            Value = testValue
        };
        var setResponse = await _client.PostAsJsonAsync("/api/client/cache/set", setRequest);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getRequest = new CacheKeyRequest { Key = testKey };
        var getResponse = await _client.PostAsJsonAsync("/api/client/cache/get", getRequest);
        
        // Assert - Get cache value
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("test").GetString().Should().Be("value");
    }

    [Fact]
    public async Task SetCacheValue_WithRelativeExpiration_SetsValueCorrectly()
    {
        // Arrange
        string testKey = "expiration-test-key";
        var testValue = JsonDocument.Parse("{\"test\": \"expiration\"}").RootElement;
        int relativeExpirationMinutes = 60; // 1 hour

        // Act - Set cache value with expiration
        var setRequest = new CacheSetRequest
        {
            Key = testKey,
            Value = testValue,
            RelativeExpirationMinutes = relativeExpirationMinutes
        };
        var setResponse = await _client.PostAsJsonAsync("/api/client/cache/set", setRequest);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getRequest = new CacheKeyRequest { Key = testKey };
        var getResponse = await _client.PostAsJsonAsync("/api/client/cache/get", getRequest);
        
        // Assert - Get cache value
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("test").GetString().Should().Be("expiration");
    }

    [Fact]
    public async Task SetCacheValue_WithSlidingExpiration_SetsValueCorrectly()
    {
        // Arrange
        string testKey = "sliding-test-key";
        var testValue = JsonDocument.Parse("{\"test\": \"sliding\"}").RootElement;
        int slidingExpirationMinutes = 30; // 30 minutes

        // Act - Set cache value with sliding expiration
        var setRequest = new CacheSetRequest
        {
            Key = testKey,
            Value = testValue,
            SlidingExpirationMinutes = slidingExpirationMinutes
        };
        var setResponse = await _client.PostAsJsonAsync("/api/client/cache/set", setRequest);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getRequest = new CacheKeyRequest { Key = testKey };
        var getResponse = await _client.PostAsJsonAsync("/api/client/cache/get", getRequest);
        
        // Assert - Get cache value
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("test").GetString().Should().Be("sliding");
    }

    [Fact]
    public async Task SetCacheValue_WithBothExpirations_SetsValueCorrectly()
    {
        // Arrange
        string testKey = "both-expirations-key";
        var testValue = JsonDocument.Parse("{\"test\": \"both\"}").RootElement;
        int relativeExpirationMinutes = 120; // 2 hours
        int slidingExpirationMinutes = 15; // 15 minutes

        // Act - Set cache value with both expiration types
        var setRequest = new CacheSetRequest
        {
            Key = testKey,
            Value = testValue,
            RelativeExpirationMinutes = relativeExpirationMinutes,
            SlidingExpirationMinutes = slidingExpirationMinutes
        };
        var setResponse = await _client.PostAsJsonAsync("/api/client/cache/set", setRequest);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getRequest = new CacheKeyRequest { Key = testKey };
        var getResponse = await _client.PostAsJsonAsync("/api/client/cache/get", getRequest);
        
        // Assert - Get cache value
        getResponse.EnsureSuccessStatusCode();
        var content = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        content.GetProperty("test").GetString().Should().Be("both");
    }

    [Fact]
    public async Task DeleteCacheValue_WhenKeyExists_ReturnsNoContent()
    {
        // Arrange
        string testKey = "delete-test-key";
        var testValue = JsonDocument.Parse("{\"test\": \"delete\"}").RootElement;
        
        var setRequest = new CacheSetRequest
        {
            Key = testKey,
            Value = testValue
        };
        await _client.PostAsJsonAsync("/api/client/cache/set", setRequest);

        // Act
        var deleteRequest = new CacheKeyRequest { Key = testKey };
        var response = await _client.PostAsJsonAsync("/api/client/cache/delete", deleteRequest);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        
        // Verify key is deleted
        var getRequest = new CacheKeyRequest { Key = testKey };
        var getResponse = await _client.PostAsJsonAsync("/api/client/cache/get", getRequest);
        getResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
} 