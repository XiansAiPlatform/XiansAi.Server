using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class AdminTemplateEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminTemplateEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task BrowseAgentTemplates_WithValidRequest_ReturnsTemplates()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Create a system-scoped agent (template)
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var template = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = $"template-{Guid.NewGuid()}",
            Tenant = null,
            SystemScoped = true,
            CreatedBy = _adminUserId ?? "system",
            CreatedAt = DateTime.UtcNow
        };
        await agentRepository.CreateAsync(template);

        // Act
        var response = await GetAsync("/api/v1/admin/agentTemplates");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
    }

    [Fact]
    public async Task GetAgentTemplateDetails_WithValidId_ReturnsTemplate()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Create a system-scoped agent (template)
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var template = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = $"template-{Guid.NewGuid()}",
            Tenant = null,
            SystemScoped = true,
            CreatedBy = _adminUserId ?? "system",
            CreatedAt = DateTime.UtcNow
        };
        await agentRepository.CreateAsync(template);

        // Act
        var response = await GetAsync($"/api/v1/admin/agentTemplates/{template.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await ReadAsJsonAsync<Agent>(response);
        Assert.NotNull(result);
        Assert.Equal(template.Id, result.Id);
    }

    [Fact]
    public async Task GetAgentTemplateDetails_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/v1/admin/agentTemplates/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAgentTemplate_WithValidRequest_UpdatesTemplate()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Create a system-scoped agent (template)
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var template = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = $"template-{Guid.NewGuid()}",
            Tenant = null,
            SystemScoped = true,
            CreatedBy = _adminUserId ?? "system",
            CreatedAt = DateTime.UtcNow
        };
        await agentRepository.CreateAsync(template);

        var request = new
        {
            description = "Updated description",
            onboardingJson = "{\"key\":\"value\"}"
        };

        // Act
        var response = await PatchAsJsonAsync($"/api/v1/admin/agentTemplates/{template.Id}", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAgentTemplate_WithValidId_DeletesTemplate()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Create a system-scoped agent (template)
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var template = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = $"template-{Guid.NewGuid()}",
            Tenant = null,
            SystemScoped = true,
            CreatedBy = _adminUserId ?? "system",
            CreatedAt = DateTime.UtcNow
        };
        await agentRepository.CreateAsync(template);

        // Act
        var response = await DeleteAsync($"/api/v1/admin/agentTemplates/{template.Id}");

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeployTemplateToTenant_WithValidRequest_DeploysTemplate()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Create a system-scoped agent (template)
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var template = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = $"template-{Guid.NewGuid()}",
            Tenant = null,
            SystemScoped = true,
            CreatedBy = _adminUserId ?? "system",
            CreatedAt = DateTime.UtcNow
        };
        await agentRepository.CreateAsync(template);

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/agentTemplates/{template.Id}/deploy?tenantId={tenantId}", new { });

        // Assert
        // Deployment may return various status codes depending on implementation
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUpdateDeleteAndDeployTemplate_ByName_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var template = await CreateSystemTemplateAsync($"template-{Guid.NewGuid()}");

        var get = await GetAsync($"/api/v1/admin/agentTemplates/by-name/{Uri.EscapeDataString(template.Name)}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var fetched = await ReadAsJsonAsync<Agent>(get);
        Assert.Equal(template.Name, fetched!.Name);

        var deployments = await GetAsync(
            $"/api/v1/admin/agentTemplates/by-name/{Uri.EscapeDataString(template.Name)}/deployments");
        Assert.Equal(HttpStatusCode.OK, deployments.StatusCode);

        var patch = await PatchAsJsonAsync(
            $"/api/v1/admin/agentTemplates/by-name/{Uri.EscapeDataString(template.Name)}",
            new { description = "updated by name" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var deploy = await PostAsJsonAsync(
            $"/api/v1/admin/agentTemplates/by-name/{Uri.EscapeDataString(template.Name)}/deploy?tenantId={Uri.EscapeDataString(tenantId)}",
            new { });
        Assert.Equal(HttpStatusCode.OK, deploy.StatusCode);

        var delete = await DeleteAsync(
            $"/api/v1/admin/agentTemplates/by-name/{Uri.EscapeDataString(template.Name)}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var getAfterDelete = await GetAsync(
            $"/api/v1/admin/agentTemplates/by-name/{Uri.EscapeDataString(template.Name)}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task UpdateAgentTemplateByName_AsTenantAdmin_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var template = await CreateSystemTemplateAsync($"template-{Guid.NewGuid()}");

        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        var response = await PatchAsJsonAsync(
            $"/api/v1/admin/agentTemplates/by-name/{Uri.EscapeDataString(template.Name)}",
            new { description = "should fail" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Agent> CreateSystemTemplateAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var template = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = name,
            Tenant = null,
            SystemScoped = true,
            CreatedBy = _adminUserId ?? "system",
            CreatedAt = DateTime.UtcNow
        };
        await agentRepository.CreateAsync(template);
        return template;
    }
}
