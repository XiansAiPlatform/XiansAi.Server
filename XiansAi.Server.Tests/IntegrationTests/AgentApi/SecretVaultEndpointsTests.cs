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

    [Fact]
    public async Task FetchByTenants_ReturnsValuePerTenant_AndNullForMisses()
    {
        var key = $"batch-secret-{Guid.NewGuid()}";
        foreach (var (tenantId, value) in new[] { (TestTenantId, "sk_live_aaa"), ("99x.io", "sk_live_bbb") })
        {
            var create = await _client.PostAsJsonAsync("/api/agent/secrets",
                new AgentSecretVaultCreateRequest { Key = key, Value = value, TenantId = tenantId });
            Assert.True(create.IsSuccessStatusCode, $"Create failed with {create.StatusCode}");
        }

        var response = await _client.PostAsJsonAsync("/api/agent/secrets/fetch/by-tenants",
            new AgentSecretVaultFetchByTenantsRequest { Key = key, TenantIds = [TestTenantId, "99x.io", "missing-tenant"] });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
        Assert.NotNull(body);
        Assert.Equal(3, body!.Count);
        Assert.Equal("sk_live_aaa", body[TestTenantId]);
        Assert.Equal("sk_live_bbb", body["99x.io"]);
        Assert.Null(body["missing-tenant"]);
    }

    [Fact]
    public async Task FetchByTenants_WithActivationName_ReturnsOnlyMatchingScope()
    {
        // Key uniqueness is enforced per tenant, so the activation-scoped and tenant-level secrets
        // share a key across two different tenants.
        const string otherTenantId = "99x.io";
        var key = $"batch-activation-secret-{Guid.NewGuid()}";
        var activationName = $"act-{Guid.NewGuid():N}";
        var activationCreate = await _client.PostAsJsonAsync("/api/agent/secrets",
            new AgentSecretVaultCreateRequest { Key = key, Value = "activation-level", TenantId = TestTenantId, ActivationName = activationName });
        Assert.True(activationCreate.IsSuccessStatusCode, $"Create failed with {activationCreate.StatusCode}");
        var tenantCreate = await _client.PostAsJsonAsync("/api/agent/secrets",
            new AgentSecretVaultCreateRequest { Key = key, Value = "tenant-level", TenantId = otherTenantId });
        Assert.True(tenantCreate.IsSuccessStatusCode, $"Create failed with {tenantCreate.StatusCode}");

        var matching = await FetchByTenantsAsync(key, activationName);
        var otherActivation = await FetchByTenantsAsync(key, "other-activation");
        var noActivation = await FetchByTenantsAsync(key, activationName: null);

        // Supplied activationName: only the exact activation-scoped secret; the tenant-level one is not returned.
        Assert.Equal("activation-level", matching[TestTenantId]);
        Assert.Null(matching[otherTenantId]);
        // Different activationName: nothing matches.
        Assert.Null(otherActivation[TestTenantId]);
        Assert.Null(otherActivation[otherTenantId]);
        // Omitted activationName: only the tenant-level secret; the activation-scoped one is not returned.
        Assert.Null(noActivation[TestTenantId]);
        Assert.Equal("tenant-level", noActivation[otherTenantId]);
    }

    private async Task<Dictionary<string, string?>> FetchByTenantsAsync(string key, string? activationName)
    {
        var response = await _client.PostAsJsonAsync("/api/agent/secrets/fetch/by-tenants",
            new AgentSecretVaultFetchByTenantsRequest { Key = key, TenantIds = [TestTenantId, "99x.io"], ActivationName = activationName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
        Assert.NotNull(body);
        return body!;
    }

    private sealed record SecretFetchResponse(string Value, object? AdditionalData);
}
