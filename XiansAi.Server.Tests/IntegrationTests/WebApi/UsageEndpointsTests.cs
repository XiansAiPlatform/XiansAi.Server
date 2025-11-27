using System.Net;
using System.Net.Http.Json;
using Tests.TestUtils;

namespace Tests.IntegrationTests.WebApi;

public class UsageEndpointsTests : WebApiIntegrationTestBase
{
    public UsageEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetStatus_ReturnsOk()
    {
        var response = await GetAsync("/api/client/usage/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UsageStatusResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.MaxTokens > 0);
        Assert.True(payload.TokensRemaining <= payload.MaxTokens);
    }

    [Fact]
    public async Task UpsertLimit_ListAndDelete_Workflow()
    {
        var upsertRequest = new
        {
            tenantId = (string?)null,
            userId = "usage-test-user",
            maxTokens = 5000,
            windowSeconds = 7200,
            enabled = true
        };

        var createResponse = await PostAsJsonAsync("/api/client/usage/limits", upsertRequest);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<UsageLimitResponse>();
        Assert.NotNull(created);
        Assert.Equal(upsertRequest.userId, created!.UserId);
        Assert.Equal(upsertRequest.maxTokens, created.MaxTokens);

        var listResponse = await GetAsync("/api/client/usage/limits");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var limits = await listResponse.Content.ReadFromJsonAsync<List<UsageLimitResponse>>();
        Assert.NotNull(limits);
        Assert.Contains(limits!, l => l.Id == created.Id);

        var deleteResponse = await DeleteAsync($"/api/client/usage/limits/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    private sealed class UsageStatusResponse
    {
        public bool Enabled { get; set; }
        public long MaxTokens { get; set; }
        public long TokensUsed { get; set; }
        public long TokensRemaining { get; set; }
        public int WindowSeconds { get; set; }
    }

    private sealed class UsageLimitResponse
    {
        public string Id { get; set; } = default!;
        public string TenantId { get; set; } = default!;
        public string? UserId { get; set; }
        public long MaxTokens { get; set; }
        public int WindowSeconds { get; set; }
        public bool Enabled { get; set; }
    }
}

