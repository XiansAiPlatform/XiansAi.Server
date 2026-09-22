using System.Net;
using Shared.Data.Models.Usage;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminMetricsEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminMetricsEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetStatsTimeSeriesCategoriesThenDeleteByActivation_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, name: "front-desk");
        await SeedUsageMetricAsync(tenantId, agent.Name, activation.Name);

        var start = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));
        var agentQuery = Uri.EscapeDataString(agent.Name);

        var stats = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/metrics/stats?agentName={agentQuery}&startDate={start}&endDate={end}");
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
        var statsResult = await ReadAsJsonAsync<AdminMetricsStatsResponse>(stats);
        Assert.True(statsResult!.Summary.TotalMetricRecords >= 1);

        var timeseriesMissingType = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/metrics/timeseries?agentName={agentQuery}&category=llm&startDate={start}&endDate={end}");
        Assert.Equal(HttpStatusCode.BadRequest, timeseriesMissingType.StatusCode);

        var categories = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/metrics/categories?agentName={agentQuery}");
        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        var categoryResult = await ReadAsJsonAsync<AdminMetricsCategoriesResponse>(categories);
        Assert.Contains(categoryResult!.Categories, category =>
            string.Equals(category.Category, "llm", StringComparison.OrdinalIgnoreCase));

        var delete = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/metrics/agents/{agentQuery}/activation/{Uri.EscapeDataString(activation.Name)}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
    }

    [Fact]
    public async Task GetMetricsStats_WithoutAgentName_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var start = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/metrics/stats?startDate={start}&endDate={end}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
