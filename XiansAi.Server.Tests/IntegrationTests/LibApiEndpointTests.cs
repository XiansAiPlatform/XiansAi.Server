using System.Net.Http.Json;
using System.Text.Json;
using XiansAi.Server.Tests.TestUtils;

namespace XiansAi.Server.Tests.IntegrationTests;

public class LibApiEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public LibApiEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetCacheValue_WhenKeyNotFound_ReturnsNoContent()
    {
        // Arrange
        string testKey = "non-existent-key";

        // Act
        var response = await _client.GetAsync($"/api/client/cache/{testKey}");

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
        var setResponse = await _client.PostAsJsonAsync($"/api/client/cache/{testKey}", testValue);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getResponse = await _client.GetAsync($"/api/client/cache/{testKey}");
        
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
        var setResponse = await _client.PostAsJsonAsync(
            $"/api/client/cache/{testKey}?relativeExpirationMinutes={relativeExpirationMinutes}", 
            testValue);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getResponse = await _client.GetAsync($"/api/client/cache/{testKey}");
        
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
        var setResponse = await _client.PostAsJsonAsync(
            $"/api/client/cache/{testKey}?slidingExpirationMinutes={slidingExpirationMinutes}", 
            testValue);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getResponse = await _client.GetAsync($"/api/client/cache/{testKey}");
        
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
        var setResponse = await _client.PostAsJsonAsync(
            $"/api/client/cache/{testKey}?relativeExpirationMinutes={relativeExpirationMinutes}&slidingExpirationMinutes={slidingExpirationMinutes}", 
            testValue);
        
        // Assert - Set cache value
        setResponse.EnsureSuccessStatusCode();
        
        // Act - Get cache value
        var getResponse = await _client.GetAsync($"/api/client/cache/{testKey}");
        
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
        await _client.PostAsJsonAsync($"/api/client/cache/{testKey}", testValue);

        // Act
        var response = await _client.DeleteAsync($"/api/client/cache/{testKey}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        
        // Verify key is deleted
        var getResponse = await _client.GetAsync($"/api/client/cache/{testKey}");
        getResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
} 