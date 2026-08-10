using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Providers.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Email is unique across the system, and this is the single creation path everything funnels
/// through.
///
/// The invariant is load-bearing rather than cosmetic: <c>GetByUserEmailAsync</c> returns the first
/// match, and both certificate authentication and ownership transfer accept an email in place of a
/// user id. A second record sharing an email makes those resolve to an arbitrary one of the two.
/// </summary>
public class UserManagementServiceEmailUniquenessTests
{
    private readonly Mock<IUserRepository> _userRepo = new();

    private UserManagementService BuildService()
    {
        return new UserManagementService(
            _userRepo.Object,
            Mock.Of<ITenantContext>(),
            Mock.Of<IAuthMgtConnect>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IInvitationRepository>(),
            Mock.Of<IEmailService>(),
            Mock.Of<IJwtClaimsExtractor>(),
            Mock.Of<ITokenValidationCache>(),
            NullLogger<UserManagementService>.Instance);
    }

    private void ArrangeCreatable()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.GetByUserEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.GetAnyUserAsync()).ReturnsAsync(new User { UserId = "someone-else" });
        _userRepo.Setup(x => x.CreateAsync(It.IsAny<User>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task CreateNewUser_RefusesAnEmailThatBelongsToADifferentSubject()
    {
        ArrangeCreatable();
        _userRepo.Setup(x => x.GetByUserEmailAsync("taken@example.com"))
            .ReturnsAsync(new User { UserId = "some-other-subject", Email = "taken@example.com" });

        var result = await BuildService().CreateNewUser(new UserDto
        {
            UserId = "google-subject-123",
            Email = "taken@example.com",
            Name = "Test User"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewUser_AcceptsAnUnusedEmail()
    {
        ArrangeCreatable();

        var result = await BuildService().CreateNewUser(new UserDto
        {
            UserId = "google-subject-123",
            Email = "fresh@example.com",
            Name = "Test User"
        });

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.CreateAsync(It.Is<User>(u => u.UserId == "google-subject-123")), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateNewUser_DoesNotCompareRecordsThatHaveNoEmail(string email)
    {
        // Comparing blank emails would make every record with one collide with the first, which
        // would refuse creation for all of them.
        ArrangeCreatable();

        var result = await BuildService().CreateNewUser(new UserDto
        {
            UserId = "subject-with-no-email",
            Email = email,
            Name = "Test User"
        });

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.GetByUserEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewUser_StillRefusesAUserIdThatAlreadyExists()
    {
        ArrangeCreatable();
        _userRepo.Setup(x => x.GetByUserIdAsync("google-subject-123"))
            .ReturnsAsync(new User { UserId = "google-subject-123" });

        var result = await BuildService().CreateNewUser(new UserDto
        {
            UserId = "google-subject-123",
            Email = "fresh@example.com",
            Name = "Test User"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
    }
}
