using System.Net;
using Features.AdminApi.Endpoints;
using Shared.Auth;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminParticipantsEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminParticipantsEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetParticipantByEmailAndUserId_ReturnsTenantMembership()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var member = await CreateTestUserWithRoleAsync($"member-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);

        var byEmail = await GetAsync($"/api/v1/admin/participants/{Uri.EscapeDataString(member.Email)}");
        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
        var emailResult = await ReadAsJsonAsync<ParticipantTenantsResponse>(byEmail);
        Assert.Contains(emailResult!.Tenants, tenant => tenant.TenantId == tenantId);

        var byUserId = await GetAsync($"/api/v1/admin/participants/by-user-id/{Uri.EscapeDataString(member.UserId)}");
        Assert.Equal(HttpStatusCode.OK, byUserId.StatusCode);
        var idResult = await ReadAsJsonAsync<ParticipantTenantsResponse>(byUserId);
        Assert.Contains(idResult!.Tenants, tenant => tenant.TenantId == tenantId);
        Assert.Equal(SystemRoles.TenantUser, idResult.Tenants.Single(tenant => tenant.TenantId == tenantId).Role);
    }

    [Fact]
    public async Task GetParticipant_WithUnknownEmail_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/participants/missing-{Guid.NewGuid()}@example.com");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetParticipant_WithInvalidEmail_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync("/api/v1/admin/participants/not-an-email");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetParticipant_AsTenantAdmin_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);
        var member = await CreateTestUserWithRoleAsync($"member-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);

        var response = await GetAsync($"/api/v1/admin/participants/{Uri.EscapeDataString(member.Email)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
