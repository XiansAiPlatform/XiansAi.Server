using Features.AdminApi.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Repositories;
using Xunit;

namespace Tests.UnitTests.Features.AdminApi.Auth;

public class AdminActingUserResolverTests
{
    private const string Jwt = "header.payload.signature";
    private const string TenantId = "tenant-1";
    private const string ProviderUserId = "provider-subject-abc123";
    private const string CanonicalUserId = "keycloak|provider-subject-abc123";

    private readonly Mock<IDynamicOidcValidator> _oidcValidator = new();
    private readonly Mock<IUserRepository> _userRepository = new();

    private AdminActingUserResolver BuildResolver() =>
        new(_oidcValidator.Object, _userRepository.Object, NullLogger<AdminActingUserResolver>.Instance);

    [Fact]
    public async Task NoToken_ReturnsNotAttempted_AndNeverCallsTheValidator()
    {
        var result = await BuildResolver().ResolveAsync(null, TenantId);

        Assert.False(result.Attempted);
        _oidcValidator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EmptyToken_ReturnsNotAttempted()
    {
        var result = await BuildResolver().ResolveAsync(string.Empty, TenantId);

        Assert.False(result.Attempted);
    }

    [Fact]
    public async Task ValidToken_ValidatesAgainstTheAdminConsolePseudoTenant()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync(AdminActingUserResolver.AdminConsolePseudoTenant, Jwt))
            .ReturnsAsync(OidcValidationResult.Ok(CanonicalUserId, ProviderUserId, "https://login.example.com", "user@example.com", "Test User"));
        _userRepository.Setup(x => x.GetUserRolesAsync(ProviderUserId, TenantId)).ReturnsAsync(new List<string> { "TenantUser" });

        var result = await BuildResolver().ResolveAsync(Jwt, TenantId);

        _oidcValidator.Verify(x => x.ValidateAsync(AdminActingUserResolver.AdminConsolePseudoTenant, Jwt), Times.Once);
        Assert.True(result.Attempted);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ValidToken_ResolvesToProviderUserId_NotCanonicalUserId()
    {
        // AdminApi's ACL grants are stored/compared as the platform's raw User.UserId
        // (ProviderUserId), not UserApi's `provider|subject` CanonicalUserId - using the wrong one
        // would silently stop matching every existing grant.
        _oidcValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), Jwt))
            .ReturnsAsync(OidcValidationResult.Ok(CanonicalUserId, ProviderUserId, "https://login.example.com", "user@example.com", "Test User"));
        _userRepository.Setup(x => x.GetUserRolesAsync(ProviderUserId, TenantId)).ReturnsAsync(new List<string>());

        var result = await BuildResolver().ResolveAsync(Jwt, TenantId);

        Assert.Equal(ProviderUserId, result.CanonicalUserId);
        Assert.NotEqual(CanonicalUserId, result.CanonicalUserId);
    }

    [Fact]
    public async Task ValidToken_ResolvesRealTenantRolesForTheTargetTenant()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), Jwt))
            .ReturnsAsync(OidcValidationResult.Ok(CanonicalUserId, ProviderUserId, "https://login.example.com", "user@example.com", "Test User"));
        _userRepository
            .Setup(x => x.GetUserRolesAsync(ProviderUserId, TenantId))
            .ReturnsAsync(new List<string> { "TenantParticipant" });

        var result = await BuildResolver().ResolveAsync(Jwt, TenantId);

        Assert.Equal(new[] { "TenantParticipant" }, result.UserRoles);
    }

    [Fact]
    public async Task UnrecognizedUser_ResolvesToEmptyRoles_NotAnError()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), Jwt))
            .ReturnsAsync(OidcValidationResult.Ok(CanonicalUserId, ProviderUserId, "https://login.example.com", null, null));
        _userRepository.Setup(x => x.GetUserRolesAsync(ProviderUserId, TenantId)).ReturnsAsync(new List<string>());

        var result = await BuildResolver().ResolveAsync(Jwt, TenantId);

        Assert.True(result.Success);
        Assert.Empty(result.UserRoles!);
    }

    [Fact]
    public async Task InvalidToken_ReturnsFailure_NotNotAttempted()
    {
        // A present-but-invalid token must fail the whole request, not be treated the same as no
        // token at all - that's the "fail closed, not silent fallback" decision.
        _oidcValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), Jwt))
            .ReturnsAsync(OidcValidationResult.Fail("signature invalid"));

        var result = await BuildResolver().ResolveAsync(Jwt, TenantId);

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task InvalidToken_NeverCallsTheUserRepository()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), Jwt))
            .ReturnsAsync(OidcValidationResult.Fail("expired"));

        await BuildResolver().ResolveAsync(Jwt, TenantId);

        _userRepository.Verify(x => x.GetUserRolesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ValidationSucceedsButNoProviderUserId_TreatedAsFailure()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), Jwt))
            .ReturnsAsync(new OidcValidationResult { Success = true, CanonicalUserId = CanonicalUserId, ProviderUserId = null });

        var result = await BuildResolver().ResolveAsync(Jwt, TenantId);

        Assert.True(result.Attempted);
        Assert.False(result.Success);
    }
}
