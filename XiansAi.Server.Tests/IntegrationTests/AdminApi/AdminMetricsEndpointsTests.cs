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

    // ---- metadata filter and breakdown --------------------------------------------------------

    private const string RunsCategory = "agent_performance";
    private const string RunsType = "runs_completed";

    /// <summary>
    /// Seeds six runs_completed records for one agent and returns the stats URL without any metadata parameter.
    ///   value 1  status=completed origin=email
    ///   value 2  status=completed origin=chat
    ///   value 4  status=failed    origin=email
    ///   value 8  status=failed    origin=chat
    ///   value 16 status=skipped   (no origin)
    ///   value 32 (no metadata at all)
    /// </summary>
    private async Task<string> SeedRunsAsync()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, name: "front-desk");

        Task Seed(double value, Dictionary<string, string>? metadata) =>
            SeedUsageMetricAsync(tenantId, agent.Name, activation.Name, RunsCategory, RunsType, value, metadata);

        await Seed(1, new() { ["status"] = "completed", ["origin"] = "email" });
        await Seed(2, new() { ["status"] = "completed", ["origin"] = "chat" });
        await Seed(4, new() { ["status"] = "failed", ["origin"] = "email" });
        await Seed(8, new() { ["status"] = "failed", ["origin"] = "chat" });
        await Seed(16, new() { ["status"] = "skipped" });
        await Seed(32, null);

        var start = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));
        return $"/api/v1/admin/tenants/{tenantId}/metrics/stats" +
               $"?agentName={Uri.EscapeDataString(agent.Name)}&startDate={start}&endDate={end}";
    }

    private async Task<AdminMetricsStatsResponse> GetStatsAsync(string url)
    {
        var response = await GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsJsonAsync<AdminMetricsStatsResponse>(response))!;
    }

    private static TypeStats RunsOf(List<CategoryStats> categories) =>
        Assert.Single(Assert.Single(categories, c => c.Category == RunsCategory).Types, t => t.Type == RunsType);

    [Fact]
    public async Task Stats_FilteredByOneMetadataValue_CountsOnlyMatchingRecords()
    {
        var url = await SeedRunsAsync();

        var stats = await GetStatsAsync(url + "&metadata=status:failed");

        Assert.Equal(2, stats.Summary.TotalMetricRecords);
        var runs = RunsOf(stats.CategoriesAndTypes);
        Assert.Equal(2, runs.Stats.Count);
        Assert.Equal(12, runs.Stats.Sum);
        Assert.Equal(2, RunsOf(Assert.Single(stats.ByActivation).CategoriesAndTypes).Stats.Count);
    }

    [Fact]
    public async Task Stats_FilteredBySeveralValuesOfOneKey_MatchesAnyOfThem()
    {
        var url = await SeedRunsAsync();

        var commaSeparated = await GetStatsAsync(url + "&metadata=status:failed,skipped");
        var repeated = await GetStatsAsync(url + "&metadata=status:failed&metadata=status:skipped");

        foreach (var stats in new[] { commaSeparated, repeated })
        {
            var runs = RunsOf(stats.CategoriesAndTypes);
            Assert.Equal(3, runs.Stats.Count);
            Assert.Equal(28, runs.Stats.Sum);
            Assert.Equal(new[] { "failed", "skipped" }, stats.Filters.Metadata!["status"]);
        }
    }

    [Fact]
    public async Task Stats_FilteredByTwoMetadataKeys_RequiresBothToMatch()
    {
        var url = await SeedRunsAsync();

        var stats = await GetStatsAsync(url + "&metadata=status:failed&metadata=origin:email");

        var runs = RunsOf(stats.CategoriesAndTypes);
        Assert.Equal(1, runs.Stats.Count);
        Assert.Equal(4, runs.Stats.Sum);
    }

    [Fact]
    public async Task Stats_FilteredByKeyNoRecordHas_ReturnsEmptyTotals()
    {
        var url = await SeedRunsAsync();

        var stats = await GetStatsAsync(url + "&metadata=decision:approved");

        Assert.Equal(0, stats.Summary.TotalMetricRecords);
        Assert.Empty(stats.CategoriesAndTypes);
        Assert.Empty(stats.ByActivation);
    }

    [Fact]
    public async Task Stats_MetadataValueMayContainColons()
    {
        var url = await SeedRunsAsync();

        // Only the first ':' separates key from value, so timestamps are usable as values.
        var stats = await GetStatsAsync(url + "&metadata=" + Uri.EscapeDataString("sentDate:2026-11-17T10:00:00Z"));

        Assert.Equal(0, stats.Summary.TotalMetricRecords);
        Assert.Equal(new[] { "2026-11-17T10:00:00Z" }, stats.Filters.Metadata!["sentDate"]);
    }

    [Fact]
    public async Task Stats_BreakdownByMetadataKey_ReturnsOneEntryPerValueThatAddUpToTheTotals()
    {
        var url = await SeedRunsAsync();

        var unfiltered = RunsOf((await GetStatsAsync(url)).CategoriesAndTypes);
        var stats = await GetStatsAsync(url + "&breakdownBy=status");
        var runs = RunsOf(stats.CategoriesAndTypes);

        // Totals are untouched by the breakdown
        Assert.Equal(unfiltered.Stats, runs.Stats);

        // One entry per value, records without the key under null (listed last)
        Assert.Equal(new[] { "completed", "failed", "skipped", null }, runs.Breakdown!.Select(b => b.Value));
        Assert.Equal(new long[] { 2, 2, 1, 1 }, runs.Breakdown!.Select(b => b.Count));
        Assert.Equal(new double[] { 3, 12, 16, 32 }, runs.Breakdown!.Select(b => b.Sum));
        Assert.Equal(new double[] { 1.5, 6, 16, 32 }, runs.Breakdown!.Select(b => b.Average));
        Assert.Equal(new double[] { 1, 4, 16, 32 }, runs.Breakdown!.Select(b => b.Min));
        Assert.Equal(new double[] { 2, 8, 16, 32 }, runs.Breakdown!.Select(b => b.Max));

        Assert.Equal(unfiltered.Stats.Count, runs.Breakdown!.Sum(b => b.Count));
        Assert.Equal(unfiltered.Stats.Sum, runs.Breakdown!.Sum(b => b.Sum));
        Assert.Equal(unfiltered.Stats.Min, runs.Breakdown!.Min(b => b.Min));
        Assert.Equal(unfiltered.Stats.Max, runs.Breakdown!.Max(b => b.Max));

        // The per-activation stats carry the same breakdown
        var byActivation = RunsOf(Assert.Single(stats.ByActivation).CategoriesAndTypes);
        Assert.Equal(runs.Breakdown, byActivation.Breakdown);

        Assert.Equal("status", stats.Filters.BreakdownBy);
        Assert.Null(stats.Filters.Metadata);
    }

    [Fact]
    public async Task Stats_BreakdownByKeySomeRecordsLack_ReportsThemUnderNull()
    {
        var url = await SeedRunsAsync();

        var runs = RunsOf((await GetStatsAsync(url + "&breakdownBy=origin")).CategoriesAndTypes);

        // "skipped" has metadata but no origin; the last record has no metadata at all
        Assert.Equal(new[] { "chat", "email", null }, runs.Breakdown!.Select(b => b.Value));
        Assert.Equal(new long[] { 2, 2, 2 }, runs.Breakdown!.Select(b => b.Count));
        Assert.Equal(new double[] { 10, 5, 48 }, runs.Breakdown!.Select(b => b.Sum));
        Assert.Equal(runs.Stats.Count, runs.Breakdown!.Sum(b => b.Count));
        Assert.Equal(runs.Stats.Sum, runs.Breakdown!.Sum(b => b.Sum));
    }

    [Fact]
    public async Task Stats_BreakdownByKeyNoRecordHas_ReportsEverythingUnderNull()
    {
        var url = await SeedRunsAsync();

        var runs = RunsOf((await GetStatsAsync(url + "&breakdownBy=decision")).CategoriesAndTypes);

        var entry = Assert.Single(runs.Breakdown!);
        Assert.Null(entry.Value);
        Assert.Equal(6, entry.Count);
        Assert.Equal(63, entry.Sum);
    }

    [Fact]
    public async Task Stats_BreakdownWithMetadataFilter_BreaksDownOnlyMatchingRecords()
    {
        var url = await SeedRunsAsync();

        var stats = await GetStatsAsync(url + "&metadata=origin:email&breakdownBy=status");
        var runs = RunsOf(stats.CategoriesAndTypes);

        Assert.Equal(2, runs.Stats.Count);
        Assert.Equal(5, runs.Stats.Sum);
        Assert.Equal(new[] { "completed", "failed" }, runs.Breakdown!.Select(b => b.Value));
        Assert.Equal(new long[] { 1, 1 }, runs.Breakdown!.Select(b => b.Count));
        Assert.Equal(new double[] { 1, 4 }, runs.Breakdown!.Select(b => b.Sum));

        Assert.Equal(new[] { "email" }, stats.Filters.Metadata!["origin"]);
        Assert.Equal("status", stats.Filters.BreakdownBy);
    }

    [Fact]
    public async Task Stats_WithoutMetadataParameters_ReturnsTodaysShapePlusNullFilterFields()
    {
        var url = await SeedRunsAsync();

        var response = await GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var stats = System.Text.Json.JsonSerializer.Deserialize<AdminMetricsStatsResponse>(json, JsonReadOptions)!;

        Assert.Equal(6, stats.Summary.TotalMetricRecords);
        Assert.Equal(63, RunsOf(stats.CategoriesAndTypes).Stats.Sum);

        // The only additions are the two null fields in filters; no per-type breakdown property appears.
        Assert.Contains("\"model\":null,\"metadata\":null,\"breakdownBy\":null}", json);
        Assert.DoesNotContain("\"breakdown\":", json);
    }

    [Theory]
    [InlineData("status.nested:open")]
    [InlineData("$where:open")]
    [InlineData("status:")]
    [InlineData(":open")]
    [InlineData("status")]
    [InlineData("")]
    [InlineData("kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk:v")]  // 65-character key
    public async Task StatsAndTimeSeries_WithMalformedMetadataFilter_ReturnBadRequestWithMessage(string metadata)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var start = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));
        var query = $"?agentName=agent&category=c&type=t&startDate={start}&endDate={end}" +
                    $"&metadata={Uri.EscapeDataString(metadata)}";

        foreach (var endpoint in new[] { "stats", "timeseries" })
        {
            var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/metrics/{endpoint}{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Metadata filter", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("status.nested")]
    [InlineData("$where")]
    [InlineData("status,origin")]
    public async Task Stats_WithInvalidBreakdownBy_ReturnsBadRequestWithMessage(string breakdownBy)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var start = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/metrics/stats?agentName=agent&startDate={start}&endDate={end}" +
            $"&breakdownBy={Uri.EscapeDataString(breakdownBy)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("breakdownBy", await response.Content.ReadAsStringAsync());
    }
}
