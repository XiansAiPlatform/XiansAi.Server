using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data;
using Shared.Repositories;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Covers the tenant email (SMTP) endpoints: save/read/delete round trip, the write-only password,
/// and which callers are allowed in. The test-email endpoint is only checked up to validation,
/// since the suite has no SMTP server to send through.
/// </summary>
public class AdminTenantSmtpConfigEndpointsTests : AdminApiIntegrationTestBase
{
    private const string Password = "smtp-test-password";

    public AdminTenantSmtpConfigEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    private static string SmtpConfig(string tenantId) => $"/api/v1/admin/tenants/{tenantId}/smtp-config";

    private static object ConfigBody(string? password) => new
    {
        host = "smtp.example.com",
        port = 587,
        security = "StartTls",
        username = "mailer",
        password,
        senderEmail = "noreply@example.com",
        senderName = "Xians"
    };

    private async Task<string> CreateTenantAsync(string role = SystemRoles.SysAdmin)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, role);
        await CreateTestTenantAsync(tenantId);
        return tenantId;
    }

    [Fact]
    public async Task SaveGetDelete_RoundTripsWithoutReturningPassword()
    {
        var tenantId = await CreateTenantAsync();

        var save = await PutAsJsonAsync(SmtpConfig(tenantId), ConfigBody(Password));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var get = await GetAsync(SmtpConfig(tenantId));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Password, body);
        using (var json = JsonDocument.Parse(body))
        {
            var root = json.RootElement;
            Assert.Equal("Tenant", root.GetProperty("source").GetString());
            Assert.Equal("smtp.example.com", root.GetProperty("host").GetString());
            Assert.True(root.GetProperty("hasPassword").GetBoolean());
            Assert.False(root.TryGetProperty("password", out _));
        }

        var delete = await DeleteAsync(SmtpConfig(tenantId));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        using (var json = JsonDocument.Parse(await delete.Content.ReadAsStringAsync()))
        {
            Assert.NotEqual("Tenant", json.RootElement.GetProperty("source").GetString());
        }

        var deleteAgain = await DeleteAsync(SmtpConfig(tenantId));
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task Save_BlankPassword_KeepsSavedPasswordEncryptedAtRest()
    {
        var tenantId = await CreateTenantAsync();

        Assert.Equal(HttpStatusCode.OK, (await PutAsJsonAsync(SmtpConfig(tenantId), ConfigBody(Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutAsJsonAsync(SmtpConfig(tenantId), ConfigBody(password: ""))).StatusCode);

        using var scope = _factory.Services.CreateScope();

        var saved = await scope.ServiceProvider.GetRequiredService<ITenantSmtpConfigRepository>().GetAsync(tenantId);
        Assert.Equal(Password, saved?.Password);

        var database = await scope.ServiceProvider.GetRequiredService<IDatabaseService>().GetDatabaseAsync();
        var raw = await database.GetCollection<BsonDocument>("tenant_smtp_config")
            .Find(new BsonDocument("tenant_id", tenantId))
            .SingleAsync();
        Assert.NotEqual(Password, raw["password"].AsString);
    }

    [Fact]
    public async Task Save_InvalidSettings_ReturnsBadRequest()
    {
        var tenantId = await CreateTenantAsync();

        var response = await PutAsJsonAsync(SmtpConfig(tenantId), new { host = "", port = 587, senderEmail = "noreply@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendTestEmail_InvalidRecipient_ReturnsBadRequest()
    {
        var tenantId = await CreateTenantAsync();

        var response = await PostAsJsonAsync($"{SmtpConfig(tenantId)}/test", new
        {
            host = "smtp.example.com",
            port = 587,
            senderEmail = "noreply@example.com",
            to = "not-an-email"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_OwnTenant_CanReadAndSave()
    {
        var tenantId = await CreateTenantAsync(SystemRoles.TenantAdmin);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(SmtpConfig(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutAsJsonAsync(SmtpConfig(tenantId), ConfigBody(Password))).StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_OtherTenant_IsRejected()
    {
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(otherTenant);
        await CreateTenantAsync(SystemRoles.TenantAdmin);

        var get = await GetAsync(SmtpConfig(otherTenant));
        var save = await PutAsJsonAsync(SmtpConfig(otherTenant), ConfigBody(Password));

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, save.StatusCode);
    }

    [Fact]
    public async Task TenantUser_IsForbidden()
    {
        var tenantId = await CreateTenantAsync(SystemRoles.TenantUser);

        var response = await GetAsync(SmtpConfig(tenantId));

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 403 or 401, got {(int)response.StatusCode}");
    }
}
