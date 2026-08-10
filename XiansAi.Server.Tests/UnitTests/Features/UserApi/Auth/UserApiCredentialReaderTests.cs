using Features.UserApi.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Tests.UnitTests.Features.UserApi.Auth;

public class UserApiCredentialReaderTests
{
    private static readonly CredentialSource[] HeaderFirst =
    [
        CredentialSource.AuthorizationHeader,
        CredentialSource.ApiKeyQueryParameter,
        CredentialSource.AccessTokenQueryParameter
    ];

    private static readonly CredentialSource[] QueryFirst =
    [
        CredentialSource.ApiKeyQueryParameter,
        CredentialSource.AccessTokenQueryParameter,
        CredentialSource.AuthorizationHeader
    ];

    private static HttpRequest BuildRequest(string? authorizationHeader = null, params (string Key, string Value)[] query)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/user/messaging/history";

        if (authorizationHeader != null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }

        if (query.Length > 0)
        {
            context.Request.Query = new QueryCollection(
                query.ToDictionary(q => q.Key, q => new StringValues(q.Value)));
        }

        return context.Request;
    }

    private static PresentedCredential? Read(HttpRequest request, CredentialSource[] order) =>
        UserApiCredentialReader.Read(request, order, NullLogger.Instance);

    [Fact]
    public void Read_ReturnsNull_WhenNoCredentialIsPresented()
    {
        Assert.Null(Read(BuildRequest(), HeaderFirst));
    }

    [Fact]
    public void Read_TakesTheBearerToken_WhenTheHeaderIsLookedAtFirst()
    {
        var request = BuildRequest("Bearer header-token", ("apikey", "query-key"));

        var credential = Read(request, HeaderFirst);

        Assert.NotNull(credential);
        var presented = credential.Value;
        Assert.Equal("header-token", presented.AccessToken);
        Assert.Equal(CredentialSource.AuthorizationHeader, presented.Source);
        Assert.True(presented.IsFromAuthorizationHeader);
    }

    [Fact]
    public void Read_TakesTheQueryParameter_WhenTheQueryIsLookedAtFirst()
    {
        // The WebSocket handshake keeps this order because browser clients cannot set headers.
        var request = BuildRequest("Bearer header-token", ("apikey", "query-key"));

        var credential = Read(request, QueryFirst);

        Assert.NotNull(credential);
        var presented = credential.Value;
        Assert.Equal("query-key", presented.AccessToken);
        Assert.Equal(CredentialSource.ApiKeyQueryParameter, presented.Source);
        Assert.False(presented.IsFromAuthorizationHeader);
    }

    [Fact]
    public void Read_FallsBackToAccessTokenQueryParameter_WhenNothingElseIsPresent()
    {
        var request = BuildRequest(query: ("access_token", "jwt-token"));

        var credential = Read(request, HeaderFirst);

        Assert.NotNull(credential);
        var presented = credential.Value;
        Assert.Equal("jwt-token", presented.AccessToken);
        Assert.Equal(CredentialSource.AccessTokenQueryParameter, presented.Source);
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    [InlineData("header-token-without-scheme")]
    public void Read_IgnoresAnAuthorizationHeaderThatIsNotAUsableBearerToken(string authorizationHeader)
    {
        var request = BuildRequest(authorizationHeader, ("apikey", "query-key"));

        var credential = Read(request, HeaderFirst);

        Assert.NotNull(credential);
        Assert.Equal("query-key", credential.Value.AccessToken);
    }
}
