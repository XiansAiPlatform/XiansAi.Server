using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Features.AdminApi.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// A SysAdmin naming a tenant that does not exist is an expected probe: the Agent API asks for the
/// lowercase form of a tenant id before its legacy casing. These tests pin that it is answered 404
/// on both auth paths without an error-level log entry, which the exception middleware used to write
/// (with a stack trace) for every probe.
/// </summary>
public class AdminUnknownTenantTests : AdminApiIntegrationTestBase
{
    private const string SysAdminIdToken = "sysadmin-id-token";

    private readonly CapturingLoggerProvider _logs = new();
    private readonly Mock<IDynamicOidcValidator> _oidcValidator = new();
    private WebApplicationFactory<Program>? _host;

    public AdminUnknownTenantTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    public override async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
        await base.DisposeAsync();
    }

    /// <summary>
    /// A host that records its logs and accepts <see cref="SysAdminIdToken"/> as a valid ID token for
    /// <paramref name="sysAdminUserId"/>, so the keyless path runs its real resolver without an
    /// identity provider.
    /// </summary>
    private HttpClient CreateClient(string? sysAdminUserId = null)
    {
        if (sysAdminUserId != null)
        {
            _oidcValidator
                .Setup(x => x.ValidateAsync(AdminKeylessUserResolver.AdminConsolePseudoTenant, SysAdminIdToken))
                .ReturnsAsync(OidcValidationResult.Ok(
                    sysAdminUserId, sysAdminUserId, "https://login.example.com",
                    $"{sysAdminUserId}@test.com", "SysAdmin",
                    emailVerification: new EmailVerificationResult(true, $"{sysAdminUserId}@test.com", "verified email")));
        }

        _host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(_logs);
            services.AddScoped<IAdminKeylessUserResolver>(sp => new AdminKeylessUserResolver(
                _oidcValidator.Object,
                sp.GetRequiredService<IUserRepository>(),
                sp.GetRequiredService<ITenantCacheService>(),
                sp.GetRequiredService<ILogger<AdminKeylessUserResolver>>()));
        }));

        // Not the base class's RetryHttpClient: it retries 401s, and these tests assert on them.
        return _host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });
    }

    private static HttpRequestMessage WithApiKey(string uri, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static HttpRequestMessage WithIdToken(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add(AdminEndpointAuthenticationHandler.UserTokenHeaderName, SysAdminIdToken);
        return request;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task<string> CreateSysAdminApiKeyAsync()
    {
        var keyTenant = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(keyTenant);
        return await CreateTestApiKeyWithAdminRoleAsync(keyTenant, $"sysadmin-{Guid.NewGuid()}", SystemRoles.SysAdmin);
    }

    private void AssertNoErrorLogged() =>
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);

    private void AssertSingleWarningNaming(string tenantId) =>
        Assert.Single(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(tenantId));

    [Fact]
    public async Task ApiKeyPath_NonExistentTenant_Returns404_WithoutAnErrorLog()
    {
        var apiKey = await CreateSysAdminApiKeyAsync();
        var client = CreateClient();
        var missingTenant = $"cust{Guid.NewGuid():N}";

        var response = await client.SendAsync(WithApiKey($"/api/v1/admin/tenants/{missingTenant}", apiKey));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("Not found", body.GetProperty("error").GetString());
        Assert.Equal($"Tenant '{missingTenant}' was not found", body.GetProperty("message").GetString());
        AssertNoErrorLogged();
        AssertSingleWarningNaming(missingTenant);
    }

    [Fact]
    public async Task ApiKeyPath_NonExistentTenantInTheHeader_Returns404()
    {
        var apiKey = await CreateSysAdminApiKeyAsync();
        var client = CreateClient();
        var missingTenant = $"cust{Guid.NewGuid():N}";

        var request = WithApiKey("/api/v1/admin/tenants", apiKey);
        request.Headers.Add("X-Tenant-Id", missingTenant);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNoErrorLogged();
    }

    [Fact]
    public async Task ApiKeyPath_MalformedTenantId_Returns400_WithoutAnErrorLog()
    {
        var apiKey = await CreateSysAdminApiKeyAsync();
        var client = CreateClient();

        var response = await client.SendAsync(WithApiKey("/api/v1/admin/tenants/bad!tenant", apiKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("Bad request", body.GetProperty("error").GetString());
        Assert.Equal("Invalid tenant ID format", body.GetProperty("message").GetString());
        AssertNoErrorLogged();
    }

    [Fact]
    public async Task ApiKeyPath_ExistingTenant_IsUnchanged()
    {
        var apiKey = await CreateSysAdminApiKeyAsync();
        var client = CreateClient();
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);

        var response = await client.SendAsync(WithApiKey($"/api/v1/admin/tenants/{tenantId}", apiKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoErrorLogged();
    }

    [Fact]
    public async Task ApiKeyPath_InvalidKey_IsStill401_WithTheExistingReason()
    {
        var client = CreateClient();

        var response = await client.SendAsync(
            WithApiKey($"/api/v1/admin/tenants/cust{Guid.NewGuid():N}", "sk-Xnai-not-a-real-key"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid API key", (await BodyOf(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task ApiKeyPath_NonAdminKey_IsStill401_WithTheExistingReason()
    {
        var keyTenant = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(keyTenant);
        var apiKey = await CreateTestApiKeyWithAdminRoleAsync(keyTenant, $"user-{Guid.NewGuid()}", SystemRoles.TenantUser);
        var client = CreateClient();

        var response = await client.SendAsync(WithApiKey($"/api/v1/admin/tenants/cust{Guid.NewGuid():N}", apiKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("User does not have required admin role", (await BodyOf(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task IdTokenPath_NonExistentTenant_Returns404_WithoutAnErrorLog()
    {
        var sysAdminUserId = $"sysadmin-{Guid.NewGuid()}";
        await CreateTestUserWithRoleAsync(sysAdminUserId, TestTenantId, SystemRoles.SysAdmin);
        var client = CreateClient(sysAdminUserId);
        var missingTenant = $"cust{Guid.NewGuid():N}";

        var response = await client.SendAsync(WithIdToken($"/api/v1/admin/tenants/{missingTenant}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("Not found", body.GetProperty("error").GetString());
        Assert.Equal($"Tenant '{missingTenant}' was not found", body.GetProperty("message").GetString());
        AssertNoErrorLogged();
        AssertSingleWarningNaming(missingTenant);
    }

    [Fact]
    public async Task IdTokenPath_MalformedTenantId_Returns400_WithoutAnErrorLog()
    {
        var sysAdminUserId = $"sysadmin-{Guid.NewGuid()}";
        await CreateTestUserWithRoleAsync(sysAdminUserId, TestTenantId, SystemRoles.SysAdmin);
        var client = CreateClient(sysAdminUserId);

        var response = await client.SendAsync(WithIdToken("/api/v1/admin/tenants/bad!tenant"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoErrorLogged();
    }
}
