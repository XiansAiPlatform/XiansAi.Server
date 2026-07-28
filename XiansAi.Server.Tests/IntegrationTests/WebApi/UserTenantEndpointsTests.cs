using System.Net;
using System.Net.Http.Json;
using Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Shared.Repositories;
using Shared.Data.Models;
using MongoDB.Bson;

namespace Tests.IntegrationTests.WebApi;

public class UserTenantEndpointsTests : WebApiIntegrationTestBase
{
    public UserTenantEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetTenantInvitations_ReturnsInvitations()
    {
        // Act
        var response = await GetAsync("/api/user-tenants/invitations");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var invitations = await response.Content.ReadFromJsonAsync<List<InvitationInfo>>();
        Assert.NotNull(invitations);
    }

    [Fact]
    public async Task DeleteInvitation_WithValidToken_DeletesInvitation()
    {
        // Arrange
        var invitation = await CreateTestInvitationAsync();

        // Act
        var response = await DeleteAsync($"/api/user-tenants/invitations/{invitation.Token}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInvitation_WithInvalidToken_ReturnsNotFound()
    {
        // Act
        var response = await DeleteAsync("/api/user-tenants/invitations/invalid-token");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SearchUsers_WithValidQuery_ReturnsMatchingUsers()
    {
        // Arrange
        await CreateTestUserWithTenantsAsync("searchable-user-1", "searchable1@example.com");

        // Act
        var response = await GetAsync("/api/user-tenants/search?query=searchable");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<List<UserSearchResult>>();
        Assert.NotNull(users);
    }

    private async Task<User> CreateTestUserWithTenantsAsync(string name, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = ObjectId.GenerateNewId().ToString(),
            Name = name,
            Email = email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TenantRoles = new List<TenantRole>
            {
                new TenantRole
                {
                    Tenant = TestTenantId,
                    Roles = new List<string> { "User" },
                    IsApproved = true
                }
            }
        };

        await userRepository.CreateAsync(user);
        return user;
    }

    private async Task<Invitation> CreateTestInvitationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var invitationRepository = scope.ServiceProvider.GetRequiredService<IInvitationRepository>();

        var invitation = new Invitation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Email = "invitation@example.com",
            Token = Guid.NewGuid().ToString(),
            TenantId = TestTenantId,
            Roles = new List<string> { "User" },
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        await invitationRepository.CreateAsync(invitation);
        return invitation;
    }
}

// DTOs used to deserialize responses in the tests above.
public class UserSearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class InvitationInfo
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
