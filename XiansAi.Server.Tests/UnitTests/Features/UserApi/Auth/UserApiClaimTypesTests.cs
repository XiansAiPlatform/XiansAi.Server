using System.Security.Claims;
using Features.UserApi.Auth;
using Shared.Auth;

namespace Tests.UnitTests.Features.UserApi.Auth;

public class UserApiClaimTypesTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData(nameof(UserType.UserApiKey), UserType.UserApiKey)]
    [InlineData(nameof(UserType.UserToken), UserType.UserToken)]
    public void ReadUserType_RoundTripsTheCredentialKind(string claimValue, UserType expected)
    {
        var principal = PrincipalWith(new Claim(UserApiClaimTypes.UserType, claimValue));

        Assert.Equal(expected, UserApiClaimTypes.ReadUserType(principal));
    }

    [Fact]
    public void ReadUserType_FallsBackToUserToken_WhenTheClaimIsAbsent()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "abc123"));

        Assert.Equal(UserType.UserToken, UserApiClaimTypes.ReadUserType(principal));
    }

    [Fact]
    public void ReadUserType_FallsBackToUserToken_WhenTheClaimIsNotAKnownUserType()
    {
        // The fallback must not be the API-key exemption, or a malformed claim would widen access.
        var principal = PrincipalWith(new Claim(UserApiClaimTypes.UserType, "not-a-user-type"));

        Assert.Equal(UserType.UserToken, UserApiClaimTypes.ReadUserType(principal));
    }
}
