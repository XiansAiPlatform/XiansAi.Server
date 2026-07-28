using System.Net;
using System.Net.Http.Json;
using Tests.TestUtils;
using Features.AgentApi.Endpoints;

namespace Tests.IntegrationTests.AgentApi;

/// <summary>
/// Smoke tests for the AgentApi secret vault group: a create/fetch round-trip through the real
/// database-backed secret store plus input validation.
/// </summary>
public class SecretVaultEndpointsTests : IntegrationTestBase
{
    public SecretVaultEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task CreateAndFetchSecret_ReturnsStoredValue()
    {
        var key = $"smoke-secret-{Guid.NewGuid()}";
        var createRequest = new AgentSecretVaultCreateRequest
        {
            Key = key,
            Value = "s3cr3t-value",
            TenantId = TestTenantId
        };

        var createResponse = await _client.PostAsJsonAsync("/api/agent/secrets", createRequest);
        Assert.True(createResponse.IsSuccessStatusCode, $"Create failed with {createResponse.StatusCode}");

        var fetchResponse = await _client.GetAsync($"/api/agent/secrets/fetch?key={key}&tenantId={TestTenantId}");
        Assert.Equal(HttpStatusCode.OK, fetchResponse.StatusCode);

        var fetched = await fetchResponse.Content.ReadFromJsonAsync<SecretFetchResponse>();
        Assert.NotNull(fetched);
        Assert.Equal("s3cr3t-value", fetched!.Value);
    }

    [Fact]
    public async Task CreateSecret_WithMissingValue_ReturnsBadRequest()
    {
        var createRequest = new AgentSecretVaultCreateRequest
        {
            Key = $"smoke-secret-{Guid.NewGuid()}",
            Value = "",
            TenantId = TestTenantId
        };

        var response = await _client.PostAsJsonAsync("/api/agent/secrets", createRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record SecretFetchResponse(string Value, object? AdditionalData);
}
