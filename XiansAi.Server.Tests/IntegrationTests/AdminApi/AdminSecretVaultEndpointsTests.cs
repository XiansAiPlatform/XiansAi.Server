using System.Net;
using Tests.TestUtils;
using Features.AdminApi.Endpoints;

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
}
