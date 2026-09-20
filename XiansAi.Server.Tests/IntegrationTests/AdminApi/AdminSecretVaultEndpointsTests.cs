using System.Net;
using Features.AdminApi.Endpoints;
using Shared.Services;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Smoke tests for the AdminApi secret vault group: create a tenant-scoped secret, look up an
/// unknown id, and validate a malformed create request.
/// </summary>
public class AdminSecretVaultEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminSecretVaultEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task CreateSecret_WithValidRequest_ReturnsSuccess()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var request = new SecretVaultCreateRequest
        {
            Key = $"smoke-secret-{Guid.NewGuid()}",
            Value = "admin-s3cr3t",
            TenantId = tenantId
        };

        var response = await PostAsJsonAsync("/api/v1/admin/secrets", request);

        Assert.True(response.IsSuccessStatusCode, $"Create failed with {response.StatusCode}");
    }

    [Fact]
    public async Task GetSecret_WithUnknownId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/secrets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateSecret_WithMissingValue_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var request = new SecretVaultCreateRequest
        {
            Key = $"smoke-secret-{Guid.NewGuid()}",
            Value = "",
            TenantId = tenantId
        };

        var response = await PostAsJsonAsync("/api/v1/admin/secrets", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateListFetchUpdateAndDeleteSecret_RoundTripsMongoWithoutExposingValue()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var key = $"vault-{Guid.NewGuid()}";

        var create = await PostAsJsonAsync("/api/v1/admin/secrets", new SecretVaultCreateRequest
        {
            Key = key,
            Value = "admin-s3cr3t",
            TenantId = tenantId
        });
        Assert.True(create.IsSuccessStatusCode, $"Create failed with {create.StatusCode}");
        var created = await ReadAsJsonAsync<SecretVaultMetadataResponse>(create);
        Assert.NotNull(created);
        Assert.Equal(key, created!.Key);
        var raw = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("admin-s3cr3t", raw);

        var list = await GetAsync($"/api/v1/admin/secrets?tenantId={Uri.EscapeDataString(tenantId)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(key, await list.Content.ReadAsStringAsync());

        var fetch = await GetAsync(
            $"/api/v1/admin/secrets/fetch?key={Uri.EscapeDataString(key)}&tenantId={Uri.EscapeDataString(tenantId)}");
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);
        Assert.DoesNotContain("admin-s3cr3t", await fetch.Content.ReadAsStringAsync());

        var get = await GetAsync($"/api/v1/admin/secrets/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var update = await PutAsJsonAsync($"/api/v1/admin/secrets/{created.Id}", new SecretVaultUpdateRequest
        {
            Value = "rotated-secret",
            TenantId = tenantId
        });
        Assert.True(update.IsSuccessStatusCode, $"Update failed with {update.StatusCode}");

        var delete = await DeleteAsync($"/api/v1/admin/secrets/{created.Id}");
        Assert.True(delete.IsSuccessStatusCode, $"Delete failed with {delete.StatusCode}");

        var getAfterDelete = await GetAsync($"/api/v1/admin/secrets/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }
}
