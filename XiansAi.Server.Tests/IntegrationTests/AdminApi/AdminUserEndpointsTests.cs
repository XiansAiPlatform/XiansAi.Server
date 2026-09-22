using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shared.Auth;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminUserEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminUserEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task CreateGetPatchRemoveRoleAndDelete_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var existing = await CreateTestUserWithRoleAsync($"member-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);

        var createResponse = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/users", new
        {
            userId = existing.UserId,
            role = SystemRoles.TenantParticipant
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadAsJsonAsync<TenantParticipantUser>(createResponse);
        Assert.NotNull(created);
        Assert.Equal(existing.UserId, created!.UserId);
        Assert.Contains(SystemRoles.TenantParticipant, created.Roles);

        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/users/{existing.UserId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await ReadAsJsonAsync<TenantParticipantUser>(getResponse);
        Assert.Equal(existing.Email, fetched!.Email);

        var listResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/users?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadAsJsonAsync<PagedParticipantResult>(listResponse);
        Assert.NotNull(list);
        Assert.Contains(list!.Users, user => user.UserId == existing.UserId);

        var patchResponse = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/users/{existing.UserId}", new
        {
            name = "Renamed Participant"
        });
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patched = await ReadAsJsonAsync<TenantParticipantUser>(patchResponse);
        Assert.Equal("Renamed Participant", patched!.Name);

        var removeRole = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/users/{existing.UserId}/roles/{SystemRoles.TenantParticipant}");
        Assert.Equal(HttpStatusCode.NoContent, removeRole.StatusCode);

        var afterRemove = await ReadAsJsonAsync<TenantParticipantUser>(
            await GetAsync($"/api/v1/admin/tenants/{tenantId}/users/{existing.UserId}"));
        Assert.DoesNotContain(SystemRoles.TenantParticipant, afterRemove!.Roles);

        var deleteResponse = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/users/{existing.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await GetAsync($"/api/v1/admin/tenants/{tenantId}/users/{existing.UserId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task GetTenantUser_WithUnknownUser_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/users/unknown-user-{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenantUser_WithInvalidRole_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/users", new
        {
            email = "person@example.com",
            name = "Person",
            role = "NotARealRole"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenantUser_AsTenantAdmin_AgainstOtherTenant_ReturnsForbidden()
    {
        var tenantA = $"test-tenant-{Guid.NewGuid()}";
        var tenantB = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantA);
        await CreateTestTenantAsync(tenantB);
        await ConfigureAdminApiClientAsync(tenantA, SystemRoles.TenantAdmin);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantB}/users?tenantId={tenantA}", new
        {
            email = "cross@example.com",
            name = "Cross",
            role = SystemRoles.TenantParticipant
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatchTenantUser_WhenTargetIsSysAdminAndCallerIsNot_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        var sysAdmin = await CreateTestUserWithRoleAsync($"sysadmin-{Guid.NewGuid()}", tenantId, SystemRoles.SysAdmin);
        sysAdmin.TenantRoles.Add(new Shared.Data.Models.TenantRole
        {
            Tenant = tenantId,
            Roles = new List<string> { SystemRoles.TenantUser },
            IsApproved = true
        });
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Shared.Repositories.IUserRepository>();
            await users.UpdateAsync(sysAdmin.UserId, sysAdmin);
        }

        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);

        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/users/{sysAdmin.UserId}", new
        {
            name = "Should Fail"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
