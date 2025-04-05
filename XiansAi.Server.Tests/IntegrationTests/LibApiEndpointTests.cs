using System.Net.Http.Json;
using System.Text.Json;
using XiansAi.Server.Tests.TestUtils;

namespace XiansAi.Server.Tests.IntegrationTests;

public class LibApiEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public LibApiEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact(Skip = "Requires proper certificate authentication")]
    public async Task GetCacheValue_WhenKeyNotFound_ReturnsNoContent()
    {
        // Arrange
        string testKey = "non-existent-key";

        // Act
        var response = await _client.GetAsync($"/api/client/cache/{testKey}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact(Skip = "Requires proper certificate authentication")]
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

    [Fact(Skip = "Requires proper certificate authentication")]
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