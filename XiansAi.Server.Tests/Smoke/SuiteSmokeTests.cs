using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Shared.Data.Models;
using Tests.IntegrationTests.AdminApi;
using Tests.TestUtils;
using Xunit;

namespace Tests.Smoke;

/// <summary>
/// Fast gate for the full suite. Boots the in-process host against Mongo2Go, checks
/// <c>/health</c>, checks that Admin auth rejects a missing key, and round-trips one tenant.
/// Does not start Temporal. <c>run-suite.sh</c> runs this first and skips the long lanes when it fails.
/// </summary>
public class SuiteSmokeTests : AdminApiIntegrationTestBase
{
    public SuiteSmokeTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task HostBoots_AuthRejectsMissingKey_AndTenantRoundTrips()
    {
        await AssertHostIsHealthyAsync();
        await AssertMissingAdminKeyIsUnauthorizedAsync();
        await AssertTenantRoundTripAsync();
    }

    private async Task AssertHostIsHealthyAsync()
    {
        var response = await GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AssertMissingAdminKeyIsUnauthorizedAsync()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        using var client = CreateRawClient();

        var response = await client.GetAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task AssertTenantRoundTripAsync()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        var tenant = await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<Tenant>(response);
        Assert.NotNull(result);
        Assert.Equal(tenant.TenantId, result.TenantId);
    }

    private HttpClient CreateRawClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
