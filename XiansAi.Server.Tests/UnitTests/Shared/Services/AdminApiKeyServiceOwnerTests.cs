using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services;

public class AdminApiKeyServiceOwnerTests
{
    private const string TenantId = "tenant-a";
    private const string UserId = "vconnect-0f8fad5b-d9cb-469f-a165-70867728950e";
    private const string Email = "admin@example.com";

    private readonly Mock<IApiKeyService> _apiKeyService = new();
    private readonly Mock<IUserRepository> _userRepo = new();

    private AdminApiKeyService BuildService() =>
        new(_apiKeyService.Object, _userRepo.Object, NullLogger<AdminApiKeyService>.Instance);

    private void AccountExists(string userId, string email, bool emailShared = false)
    {
        var user = new User { UserId = userId, Email = email };
        _userRepo.Setup(x => x.GetByUserIdOrEmailAsync(userId)).ReturnsAsync(user);
        _userRepo.Setup(x => x.GetByUserIdOrEmailAsync(email)).ReturnsAsync(user);
        _userRepo.Setup(x => x.IsEmailSharedAsync(email, userId)).ReturnsAsync(emailShared);
    }

    private static ApiKey KeyCreatedBy(string createdBy, string id = "key-1") => new()
    {
        Id = id,
        TenantId = TenantId,
        Name = id,
        HashedKey = "hash",
        CreatedAt = DateTime.UtcNow,
        CreatedBy = createdBy
    };

    [Fact]
    public async Task ResolveOwnerAsync_ByEmail_ResolvesToTheAccountUserId()
    {
        AccountExists(UserId, Email);

        var owner = await BuildService().ResolveOwnerAsync(Email);

        Assert.Equal(UserId, owner.UserId);
        Assert.Equal(new[] { UserId, Email }, owner.OwnedKeyCreators);
    }

    [Fact]
    public async Task ResolveOwnerAsync_LeavesOutTheEmail_WhenAnotherAccountHoldsIt()
    {
        AccountExists(UserId, Email, emailShared: true);

        var owner = await BuildService().ResolveOwnerAsync(UserId);

        Assert.Equal(new[] { UserId }, owner.OwnedKeyCreators);
    }

    [Fact]
    public async Task ResolveOwnerAsync_BootstrappedAdmin_WhoseUserIdIsTheEmail_KeepsTheEmail()
    {
        AccountExists(Email, Email);

        var owner = await BuildService().ResolveOwnerAsync(Email);

        Assert.Equal(Email, owner.UserId);
        Assert.Equal(new[] { Email }, owner.OwnedKeyCreators);
        _userRepo.Verify(x => x.IsEmailSharedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveOwnerAsync_FallsBackToTheSuppliedValue_WhenNoAccountMatches()
    {
        var owner = await BuildService().ResolveOwnerAsync("removed-user");

        Assert.Equal("removed-user", owner.UserId);
        Assert.Equal(new[] { "removed-user" }, owner.OwnedKeyCreators);
    }

    [Theory]
    [InlineData(UserId, true)]
    [InlineData("ADMIN@example.com", true)]
    [InlineData("someone-else@example.com", false)]
    [InlineData("", false)]
    public async Task IsNamedBy_MatchesTheUserIdOrEmail_CaseInsensitively(string value, bool expected)
    {
        AccountExists(UserId, Email);

        var owner = await BuildService().ResolveOwnerAsync(UserId);

        Assert.Equal(expected, owner.IsNamedBy(value));
    }

    [Fact]
    public async Task CreateApiKeyAsync_StoresTheResolvedUserId_NotTheEmail()
    {
        AccountExists(UserId, Email);
        var service = BuildService();
        var owner = await service.ResolveOwnerAsync(Email);

        await service.CreateApiKeyAsync(TenantId, "my-key", owner);

        _apiKeyService.Verify(x => x.CreateApiKeyAsync(TenantId, "my-key", UserId,
            null, null, null, null, null, null, null), Times.Once);
    }

    [Fact]
    public async Task ListApiKeysAsync_IncludesLegacyEmailKeys_AndExcludesOtherUsersKeys()
    {
        AccountExists(UserId, Email);
        _apiKeyService.Setup(x => x.GetApiKeysAsync(TenantId))
            .ReturnsAsync(ServiceResult<List<ApiKey>>.Success(new List<ApiKey>
            {
                KeyCreatedBy(UserId, "new-key"),
                KeyCreatedBy("ADMIN@example.com", "legacy-key"),
                KeyCreatedBy("someone-else", "other-key")
            }));
        var service = BuildService();
        var owner = await service.ResolveOwnerAsync(UserId);

        var result = await service.ListApiKeysAsync(TenantId, owner);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "new-key", "legacy-key" }, result.Data!.Select(k => k.Id));
    }

    [Fact]
    public async Task GetApiKeyAsync_ReturnsNotFound_ForAKeyTheOwnerDoesNotOwn()
    {
        AccountExists(UserId, Email);
        _apiKeyService.Setup(x => x.GetApiKeyByIdAsync("other-key", TenantId))
            .ReturnsAsync(ServiceResult<ApiKey?>.Success(KeyCreatedBy("someone-else", "other-key")));
        var service = BuildService();
        var owner = await service.ResolveOwnerAsync(UserId);

        var result = await service.GetApiKeyAsync("other-key", TenantId, owner);

        Assert.False(result.IsSuccess);
    }
}
