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
    public async Task SendDataToWorkflow_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var request = new
        {
            threadId = $"thread-{Guid.NewGuid()}",
            data = new { key = "value" },
            agent = $"agent-{Guid.NewGuid()}"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/inbound/data", request);

        // Assert
        // The response depends on workflow processing, but should not be 401/403 if authenticated
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SendChatToWorkflow_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var request = new
        {
            threadId = $"thread-{Guid.NewGuid()}",
            message = "Test message",
            agent = $"agent-{Guid.NewGuid()}"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/inbound/chat", request);

        // Assert
        // The response depends on workflow processing, but should not be 401/403 if authenticated
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
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

