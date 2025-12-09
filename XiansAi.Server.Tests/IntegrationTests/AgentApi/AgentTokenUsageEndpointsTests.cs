using System.Net;
using System.Net.Http.Json;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

public class AgentUsageEventEndpointsTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public AgentUsageEventEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetStatus_ReturnsUsageSnapshot()
    {
        var response = await _client.GetAsync("/api/agent/usage/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentUsageStatusResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.MaxTokens > 0);
        Assert.True(payload.TokensRemaining <= payload.MaxTokens);
    }

    [Fact]
    public async Task ReportUsage_IncrementsWindow()
    {
        var before = await GetStatusAsync();

        var report = new
        {
            promptTokens = 120,
            completionTokens = 30,
            model = "gpt-4o-mini",
            workflowId = "workflow-test",
            requestId = Guid.NewGuid().ToString(),
            source = "integration-test"
        };

        var reportResponse = await _client.PostAsJsonAsync("/api/agent/usage/report", report);
        Assert.Equal(HttpStatusCode.Accepted, reportResponse.StatusCode);

        var after = await GetStatusAsync();

        Assert.Equal(before.TokensUsed + 150, after.TokensUsed);
        Assert.Equal(before.TokensRemaining - 150, after.TokensRemaining);
    }

    private async Task<AgentUsageStatusResponse> GetStatusAsync()
    {
        var response = await _client.GetAsync("/api/agent/usage/status");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AgentUsageStatusResponse>();
        Assert.NotNull(payload);
        return payload!;
    }

    private sealed class AgentUsageStatusResponse
    {
        public bool Enabled { get; set; }
        public long MaxTokens { get; set; }
        public long TokensUsed { get; set; }
        public long TokensRemaining { get; set; }
        public int WindowSeconds { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEndsAt { get; set; }
        public bool IsExceeded { get; set; }
    }
}

