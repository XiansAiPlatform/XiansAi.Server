using System.Net;
using System.Text.Json;
using Features.AdminApi.Auth;
using Shared.Auth;
using Shared.Auditing;
using Shared.Data.Models;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminAuditLogEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminAuditLogEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ListTenantAuditLogs_ReturnsSeededEntry()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var performedBy = _adminUserId!;
        await SeedAuditLogAsync(tenantId, "agent.access.changed", performedBy, "front-desk");

        var list = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync();
        Assert.Contains("agent.access.changed", body);

        var performedByOptions = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs/performed-by");
        Assert.Equal(HttpStatusCode.OK, performedByOptions.StatusCode);
        Assert.Contains(performedBy, await performedByOptions.Content.ReadAsStringAsync());

        var activationNames = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs/activation-names");
        Assert.Equal(HttpStatusCode.OK, activationNames.StatusCode);
        Assert.Contains("front-desk", await activationNames.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListPlatformAuditLogs_AsSysAdmin_ReturnsSeededEntry()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        await SeedAuditLogAsync(AuditLogTenants.Platform, "user.sysadmin.granted", _adminUserId!);

        var response = await GetAsync("/api/v1/admin/platform/audit-logs?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("user.sysadmin.granted", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListPlatformAuditLogs_AsTenantAdmin_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync("/api/v1/admin/platform/audit-logs?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListTenantAuditLogs_ForPlatformTenantId_ReturnsNotFound()
    {
        var tenantId = AuditLogTenants.Platform;
        await ConfigureAdminApiClientAsync("test-tenant");

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404 or 403, got {response.StatusCode}");
    }

    [Fact]
    public async Task CreateTenantAuditLog_ViewAs_PersistsAndIsQueryable()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var adminEmail = "studio-admin@localhost.local";
        _client.HttpClient.DefaultRequestHeaders.Add(AdminOnBehalfOfBinder.HeaderName, adminEmail);

        var create = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs", ViewAsBody());

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadAsJsonAsync<AuditLogEntry>(create);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal(DomainEventTypes.ConversationViewAs, created.Action);
        Assert.Equal(adminEmail, created.ParticipantId);
        Assert.Equal(_adminUserId, created.LoggedInUser);
        Assert.Equal("participant@localhost.local", created.Details!["targetParticipantId"]?.ToString());

        var list = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs?performedBy={Uri.EscapeDataString(adminEmail)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync();
        Assert.Contains(DomainEventTypes.ConversationViewAs, body);
        Assert.Contains("targetParticipantId", body);
        Assert.Contains("participant@localhost.local", body);
        Assert.Contains(adminEmail, body);
    }

    [Fact]
    public async Task CreateTenantAuditLog_ViewAs_ReplayWithinHour_DoesNotDuplicate()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var adminEmail = "studio-admin@localhost.local";
        _client.HttpClient.DefaultRequestHeaders.Add(AdminOnBehalfOfBinder.HeaderName, adminEmail);

        var first = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs", ViewAsBody());
        var second = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs", ViewAsBody());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstEntry = await ReadAsJsonAsync<AuditLogEntry>(first);
        var secondEntry = await ReadAsJsonAsync<AuditLogEntry>(second);
        Assert.Equal(firstEntry!.Id, secondEntry!.Id);

        var list = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs?performedBy={Uri.EscapeDataString(adminEmail)}");
        using var document = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task CreateTenantAuditLog_WithoutAuth_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs", ViewAsBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenantAuditLog_AsTenantAdmin_AgainstOtherTenant_ReturnsForbidden()
    {
        var tenantA = $"test-tenant-{Guid.NewGuid()}";
        var tenantB = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantA);
        await CreateTestTenantAsync(tenantB);
        await ConfigureAdminApiClientAsync(tenantA, SystemRoles.TenantAdmin);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantB}/audit-logs?tenantId={tenantA}",
            ViewAsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenantAuditLog_ViewAs_MissingTarget_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs", new
        {
            action = DomainEventTypes.ConversationViewAs,
            description = "System admin viewed conversations as someone",
            details = new { agentName = "EmailDraftAgent" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenantAuditLog_ForPlatformTenantId_ReturnsNotFound()
    {
        await ConfigureAdminApiClientAsync("test-tenant");

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{AuditLogTenants.Platform}/audit-logs",
            ViewAsBody());

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404 or 403, got {response.StatusCode}");
    }

    private static object ViewAsBody() => new
    {
        action = DomainEventTypes.ConversationViewAs,
        description = "System admin viewed conversations as participant@localhost.local",
        activationName = "prod",
        details = new
        {
            targetParticipantId = "participant@localhost.local",
            agentName = "EmailDraftAgent"
        }
    };
}
