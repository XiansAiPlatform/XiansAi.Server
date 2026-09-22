using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminMessagingEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminMessagingEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetHistory_WithMissingQuery_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTopics_WithMissingQuery_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/topics?agentName=only-agent");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendMessage_WhenAgentHasNoWorkflow_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/send", new
        {
            agentName = agent.Name,
            activationName = "missing-activation",
            participantId = "user@example.com",
            text = "hello"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHistoryAndTopics_ThenDeleteMessages_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "inbox");
        var participantId = "reader@example.com";
        await SeedConversationAsync(tenantId, agent.Name, activation.Name, participantId, "seeded chat", "billing");

        var query = $"agentName={Uri.EscapeDataString(agent.Name)}&activationName={Uri.EscapeDataString(activation.Name)}&participantId={Uri.EscapeDataString(participantId)}";

        var historyResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history?{query}&topic=billing");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadAsStringAsync();
        Assert.Contains("seeded chat", history);

        var topicsResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/topics?{query}");
        Assert.Equal(HttpStatusCode.OK, topicsResponse.StatusCode);
        var topics = await topicsResponse.Content.ReadAsStringAsync();
        Assert.Contains("billing", topics);

        var deleteResponse = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/messaging/messages?{query}&topic=billing");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var historyAfterDelete = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history?{query}&topic=billing");
        Assert.Equal(HttpStatusCode.OK, historyAfterDelete.StatusCode);
        var after = await historyAfterDelete.Content.ReadAsStringAsync();
        Assert.DoesNotContain("seeded chat", after);
    }

    [Fact]
    public async Task DeleteMessagesByActivation_RemovesSeededThreads()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "inbox");
        await SeedConversationAsync(tenantId, agent.Name, activation.Name, "reader@example.com", "to-delete");

        var response = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/agents/{Uri.EscapeDataString(agent.Name)}/activation/{Uri.EscapeDataString(activation.Name)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("deletedCount", body);
    }

    [Fact]
    public async Task DownloadMessageFile_AgentUploadedFile_ReturnsBytesNameTypeAndAttachmentDisposition()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var bytes = Encoding.UTF8.GetBytes("agent-sent file body");
        var stored = await UploadFileAsync(
            tenantId,
            "user@example.com",
            "Q2 report (final).pdf",
            "application/pdf",
            bytes);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/files/{stored.FileId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? string.Empty;
        Assert.Contains("Q2 report (final).pdf", fileName.Trim('"'));
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DownloadMessageFile_WrongTenant_ReturnsNotFound()
    {
        var ownerTenantId = $"owner-tenant-{Guid.NewGuid()}";
        var otherTenantId = $"other-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(ownerTenantId);
        await CreateTestTenantAsync(otherTenantId);
        await ConfigureAdminApiClientAsync(otherTenantId);

        var stored = await UploadFileAsync(
            ownerTenantId,
            "user@example.com",
            "secret.txt",
            "text/plain",
            Encoding.UTF8.GetBytes("cross-tenant secret"));

        var response = await GetAsync($"/api/v1/admin/tenants/{otherTenantId}/messaging/files/{stored.FileId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadMessageFile_MissingId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/files/{MongoDB.Bson.ObjectId.GenerateNewId()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<StoredFileRef> UploadFileAsync(
        string tenantId,
        string participantId,
        string fileName,
        string contentType,
        byte[] content)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IMessageFileStorage>();
        return await storage.UploadAsync(tenantId, participantId, fileName, contentType, content);
    }
}

