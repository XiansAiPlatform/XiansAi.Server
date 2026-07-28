using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Shared.Repositories;
using Tests.TestUtils;

namespace Tests.IntegrationTests.UserApi;

/// <summary>
/// Smoke tests for the UserApi REST group. These verify API-key authentication is enforced and
/// that request validation runs before any workflow processing (which needs Temporal).
/// </summary>
public class RestEndpointsTests : IntegrationTestBase
{
    public RestEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task Send_WithoutApiKey_ReturnsUnauthorized()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var url = "/api/user/rest/send?workflow=test-workflow&type=Chat&participantId=user-1";

        var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Send_WithInvalidMessageType_ReturnsBadRequest()
    {
        var apiKey = await CreateTestApiKeyAsync();
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var url = $"/api/user/rest/send?workflow=test-workflow&type=NotAType&participantId=user-1&apikey={apiKey}";

        var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string> CreateTestApiKeyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var (apiKey, _) = await apiKeyRepository.CreateAsync(TestTenantId, "test-rest-key-" + Guid.NewGuid(), "test-user");
        return apiKey;
    }
}
