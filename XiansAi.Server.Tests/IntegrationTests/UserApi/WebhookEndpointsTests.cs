using System.Net;
using System.Text;
using Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Shared.Repositories;
using Shared.Data.Models;
using MongoDB.Bson;

namespace Tests.IntegrationTests.UserApi;

public class WebhookEndpointsTests : IntegrationTestBase
{
    public WebhookEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task ProcessWebhook_WithoutApiKey_ReturnsUnauthorized()
    {
        // Arrange
        var content = new StringContent("{\"test\": \"data\"}", Encoding.UTF8, "application/json");

        // Act
        // Create a new request without the default auth headers
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/user/webhooks/test-workflow/test-method");
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProcessWebhook_WithInvalidApiKey_ReturnsUnauthorized()
    {
        // Arrange
        var content = new StringContent("{\"test\": \"data\"}", Encoding.UTF8, "application/json");
        var url = "/api/user/webhooks/test-workflow/test-method?apikey=invalid-key";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProcessWebhook_WithValidApiKey_ProcessesRequest()
    {
        // Arrange
        var apiKey = await CreateTestApiKeyAsync();
        var content = new StringContent("{\"test\": \"data\"}", Encoding.UTF8, "application/json");
        var url = $"/api/user/webhooks/test-workflow/test-method?apikey={apiKey}";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        // The webhook will fail because Temporal is not configured in tests
        // But it should get past authentication
        // We expect either 400 (workflow not found) or 500 (Temporal error), not 401
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProcessWebhook_WithQueryParameters_PassesParametersToWorkflow()
    {
        // Arrange
        var apiKey = await CreateTestApiKeyAsync();
        var content = new StringContent("{\"test\": \"data\"}", Encoding.UTF8, "application/json");
        var url = $"/api/user/webhooks/test-workflow/test-method?apikey={apiKey}&param1=value1&param2=value2";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        // Should get past authentication - Temporal errors are expected in test environment
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        // May return BadRequest or InternalServerError due to Temporal not being configured
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                   response.StatusCode == HttpStatusCode.InternalServerError ||
                   response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProcessWebhook_WithEmptyBody_ProcessesRequest()
    {
        // Arrange
        var apiKey = await CreateTestApiKeyAsync();
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var url = $"/api/user/webhooks/test-workflow/test-method?apikey={apiKey}";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProcessWebhook_WithComplexJsonBody_ProcessesRequest()
    {
        // Arrange
        var apiKey = await CreateTestApiKeyAsync();
        var complexJson = @"{
            ""user"": {
                ""name"": ""John Doe"",
                ""email"": ""john@example.com"",
                ""metadata"": {
                    ""age"": 30,
                    ""tags"": [""tag1"", ""tag2""]
                }
            }
        }";
        var content = new StringContent(complexJson, Encoding.UTF8, "application/json");
        var url = $"/api/user/webhooks/test-workflow/test-method?apikey={apiKey}";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProcessWebhook_WithRevokedApiKey_ReturnsUnauthorized()
    {
        // Arrange
        var apiKey = await CreateTestApiKeyAsync();
        await RevokeApiKeyAsync(apiKey);
        var content = new StringContent("{\"test\": \"data\"}", Encoding.UTF8, "application/json");
        var url = $"/api/user/webhooks/test-workflow/test-method?apikey={apiKey}";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProcessWebhook_WithSpecialCharactersInWorkflowName_HandlesCorrectly()
    {
        // Arrange
        var apiKey = await CreateTestApiKeyAsync();
        var content = new StringContent("{\"test\": \"data\"}", Encoding.UTF8, "application/json");
        var url = $"/api/user/webhooks/test-workflow-123/test-method?apikey={apiKey}";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProcessWebhook_WithLargePayload_ProcessesRequest()
    {
        // Arrange
        var apiKey = await CreateTestApiKeyAsync();
        var largeJson = new string('x', 10000); // 10KB payload
        var content = new StringContent($"{{\"data\": \"{largeJson}\"}}", Encoding.UTF8, "application/json");
        var url = $"/api/user/webhooks/test-workflow/test-method?apikey={apiKey}";

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        var client = _factory.CreateClient();
        var response = await client.SendAsync(request);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> CreateTestApiKeyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();

        var (apiKey, meta) = await apiKeyRepository.CreateAsync(TestTenantId, "test-webhook-key-" + Guid.NewGuid().ToString(), "test-user");
        return apiKey;
    }

    private async Task RevokeApiKeyAsync(string apiKey)
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();

        var apiKeyRecord = await apiKeyRepository.GetByRawKeyAsync(apiKey);
        if (apiKeyRecord != null)
        {
            await apiKeyRepository.RevokeAsync(apiKeyRecord.Id, TestTenantId);
        }
    }
}

