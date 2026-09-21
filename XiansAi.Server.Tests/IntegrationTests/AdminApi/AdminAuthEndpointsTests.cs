using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Features.AdminApi.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Admin API authentication and authorization: Bearer <c>sk-Xnai-…</c> keys,
/// SysAdmin / TenantAdmin roles, tenant-scope IDOR guards, revoke, and
/// <c>X-On-Behalf-Of</c> attribution. See docs/admin-api/roles.md.
/// </summary>
public class AdminAuthEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminAuthEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task MissingBearer_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        using var client = CreateRawClient();

        var response = await client.GetAsync(Deployments(tenantId));

        await AssertUnauthorizedAsync(response, "No access token found for AdminApi Endpoint connection");
    }

    [Fact]
    public async Task QueryStringApiKey_IsIgnored_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId);
        using var client = CreateRawClient();

        var response = await client.GetAsync($"{Deployments(tenantId)}?apikey={Uri.EscapeDataString(_adminApiKey!)}");

        await AssertUnauthorizedAsync(response, "No access token found for AdminApi Endpoint connection");
    }

    [Fact]
    public async Task MalformedApiKey_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        using var client = CreateRawClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-an-xians-key");

        var response = await client.GetAsync(Deployments(tenantId));

        await AssertUnauthorizedAsync(response, "Invalid API key format");
    }

    [Fact]
    public async Task UnknownApiKey_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        using var client = CreateRawClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"sk-Xnai-{Guid.NewGuid():N}");

        var response = await client.GetAsync(Deployments(tenantId));

        await AssertUnauthorizedAsync(response, "Invalid API key");
    }

    [Theory]
    [InlineData(SystemRoles.TenantUser)]
    [InlineData(SystemRoles.TenantParticipant)]
    [InlineData(SystemRoles.TenantParticipantAdmin)]
    public async Task NonAdminKey_ReturnsUnauthorized(string role)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId, role);
        using var client = CreateAuthedClient(_adminApiKey!);

        var response = await client.GetAsync(Deployments(tenantId));

        await AssertUnauthorizedAsync(response, "User does not have required admin role");
    }

    [Fact]
    public async Task TenantAdmin_OwnTenantWithoutHeader_ReturnsOk()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        using var client = CreateAuthedClient(_adminApiKey!);

        var response = await client.GetAsync(Deployments(tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_OtherTenantInRoute_ReturnsUnauthorized()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        await ConfigureAdminApiClientAsync(ownerTenant, SystemRoles.TenantAdmin);
        using var client = CreateAuthedClient(_adminApiKey!);

        var response = await client.GetAsync(Deployments(otherTenant));

        await AssertUnauthorizedAsync(response, "Tenant ID does not match API key tenant");
    }

    [Fact]
    public async Task TenantAdmin_MismatchedRouteAndQuery_ReturnsForbidden()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        await ConfigureAdminApiClientAsync(ownerTenant, SystemRoles.TenantAdmin);
        using var client = CreateAuthedClient(_adminApiKey!);

        var response = await client.GetAsync($"{Deployments(otherTenant)}?tenantId={Uri.EscapeDataString(ownerTenant)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Tenant scope mismatch", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TenantAdmin_SysAdminOnlyListTenants_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        using var client = CreateAuthedClient(_adminApiKey!);

        var response = await client.GetAsync("/api/v1/admin/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("system administrators", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SysAdmin_OtherTenantViaRoute_ReturnsOk()
    {
        var keyTenant = $"test-tenant-{Guid.NewGuid()}";
        var targetTenant = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(keyTenant);
        await CreateTestTenantAsync(targetTenant);
        await ConfigureAdminApiClientAsync(keyTenant);
        using var client = CreateAuthedClient(_adminApiKey!);

        var response = await client.GetAsync(Deployments(targetTenant));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SysAdmin_UnknownTenant_ReturnsNotFound()
    {
        var keyTenant = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(keyTenant);
        await ConfigureAdminApiClientAsync(keyTenant);
        using var client = CreateAuthedClient(_adminApiKey!);

        var response = await client.GetAsync(Deployments($"missing-tenant-{Guid.NewGuid()}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokedApiKey_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId);
        var rawKey = _adminApiKey!;
        var keyId = await FindApiKeyIdAsync(tenantId, rawKey);
        using var scope = _factory.Services.CreateScope();
        var apiKeys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var revoked = await apiKeys.RevokeApiKeyAsync(keyId, tenantId);
        Assert.True(revoked.IsSuccess);

        using var client = CreateAuthedClient(rawKey);
        var response = await client.GetAsync(Deployments(tenantId));

        await AssertUnauthorizedAsync(response, "Invalid API key");
    }

    [Fact]
    public async Task LegacyEmailCreatedBy_Authenticates()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        var user = await CreateTestUserWithRoleAsync($"admin-user-{Guid.NewGuid()}", tenantId, SystemRoles.TenantAdmin);
        using var scope = _factory.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var (rawKey, _) = await keys.CreateAsync(tenantId, $"email-key-{Guid.NewGuid()}", user.Email);
        using var client = CreateAuthedClient(rawKey);

        var response = await client.GetAsync(Deployments(tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OnBehalfOfHeader_DoesNotChangeAuthorization_AndIsCopiedToAudit()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var member = await CreateTestUserWithRoleAsync($"writer-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);
        var uiUser = $"auth0|{Guid.NewGuid():N}";

        using var client = CreateAuthedClient(_adminApiKey!);
        client.DefaultRequestHeaders.Add(AdminOnBehalfOfBinder.HeaderName, uiUser);
        var grant = await client.PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/access/users",
            new { userId = member.UserId, level = "Read" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var entry = await WaitForAuditAsync(tenantId, DomainEventTypes.AgentAccessChanged, uiUser);
        Assert.Equal(_adminUserId, entry.LoggedInUser);
        Assert.Equal(uiUser, entry.ParticipantId);
    }

    [Fact]
    public async Task OnBehalfOfInvalidHeader_StillAuthorizes()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId);
        using var client = CreateAuthedClient(_adminApiKey!);
        client.DefaultRequestHeaders.Add(AdminOnBehalfOfBinder.HeaderName, "<script>alert(1)</script>");

        var response = await client.GetAsync(Deployments(tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private HttpClient CreateAuthedClient(string apiKey)
    {
        var client = CreateRawClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static string Deployments(string tenantId) =>
        $"/api/v1/admin/tenants/{tenantId}/agentDeployments";

    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response, string expectedMessage)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadErrorAsync(response);
        Assert.Equal("Unauthorized", body.Error);
        Assert.Equal(expectedMessage, body.Message);
    }

    private static async Task<AuthErrorBody> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<AuthErrorBody>(JsonReadOptions);
        Assert.NotNull(body);
        return body!;
    }

    private async Task<string> FindApiKeyIdAsync(string tenantId, string rawKey)
    {
        using var scope = _factory.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var match = await keys.GetByRawKeyAsync(rawKey, tenantId);
        Assert.NotNull(match);
        Assert.False(string.IsNullOrWhiteSpace(match!.Id));
        return match.Id!;
    }

    private async Task<AuditLogEntry> WaitForAuditAsync(string tenantId, string action, string participantId)
    {
        using var scope = _factory.Services.CreateScope();
        var logs = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var (entries, _) = await logs.GetFilteredAsync(tenantId, page: 1, pageSize: 50);
            var match = entries.FirstOrDefault(entry =>
                entry.Action == action && entry.ParticipantId == participantId);
            if (match != null)
            {
                return match;
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Audit row {action} for participant {participantId} did not appear.");
        return null!;
    }

    private sealed class AuthErrorBody
    {
        public string? Error { get; set; }
        public string? Message { get; set; }
    }
}
