using System.Net;
using System.Text.Json;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminFeedbackEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminFeedbackEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task SubmitListGetStatsAndDeleteFeedback_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "front-desk");
        var seeded = await SeedConversationAsync(
            tenantId,
            agent.Name,
            activation.Name,
            "user@example.com",
            text: "agent reply",
            direction: MessageDirection.Outgoing);

        var create = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/feedback", new
        {
            messageId = seeded.Message.Id,
            threadId = seeded.ThreadId,
            agentName = agent.Name,
            workflowId = seeded.WorkflowId,
            workflowType = seeded.WorkflowType,
            participantId = "user@example.com",
            starRating = 5
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var feedbackId = created.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(feedbackId));

        var list = await GetAsync($"/api/v1/admin/tenants/{tenantId}/feedback?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = await ReadAsJsonAsync<FeedbackListResponse>(list);
        Assert.Contains(listed!.Items, item => item.Id == feedbackId);

        var detail = await GetAsync($"/api/v1/admin/tenants/{tenantId}/feedback/{feedbackId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailed = await ReadAsJsonAsync<FeedbackDetailResponse>(detail);
        Assert.Equal(5, detailed!.Feedback.StarRating);

        var stats = await GetAsync($"/api/v1/admin/tenants/{tenantId}/feedback/stats");
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
        var statsResult = await ReadAsJsonAsync<FeedbackStatsResponse>(stats);
        Assert.True(statsResult!.Total >= 1);

        var delete = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/feedback/agents/{Uri.EscapeDataString(agent.Name)}/activation/{Uri.EscapeDataString(activation.Name)}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var afterDelete = await GetAsync($"/api/v1/admin/tenants/{tenantId}/feedback/{feedbackId}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_ForIncomingMessage_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "front-desk");
        var seeded = await SeedConversationAsync(
            tenantId, agent.Name, activation.Name, "user@example.com");

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/feedback", new
        {
            messageId = seeded.Message.Id,
            threadId = seeded.ThreadId,
            agentName = agent.Name,
            workflowId = seeded.WorkflowId,
            workflowType = seeded.WorkflowType,
            participantId = "user@example.com",
            starRating = 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetFeedback_WithUnknownId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/feedback/{MongoDB.Bson.ObjectId.GenerateNewId()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_WithInvalidRating_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/feedback", new
        {
            messageId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            threadId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            agentName = "any-agent",
            workflowId = "wf",
            workflowType = "Chat",
            participantId = "user@example.com",
            starRating = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
