using Features.AdminApi.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Exceptions;
using Shared.Services;
using System.Text.Encodings.Web;

namespace Tests.UnitTests.Features.AdminApi.Auth;

/// <summary>
/// Exercises <see cref="AdminEndpointAuthenticationHandler"/>'s keyless (<c>X-User-Token</c>) branch
/// directly, via <see cref="AuthenticationHandler{TOptions}"/>'s own public
/// <see cref="AuthenticationHandler{TOptions}.AuthenticateAsync"/> entry point, with
/// <see cref="IAdminKeylessUserResolver"/> mocked.
/// </summary>
public class AdminEndpointAuthenticationHandlerTests
{
    private const string TenantId = "tenant-a";
    private const string UserId = "canonical-user-id";
    private static readonly string[] Roles = ["TenantUser"];

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IApiKeyService> _apiKeyService = new();
    private readonly Mock<IAdminRoleTenantResolver> _adminRoleTenantResolver = new();
    private readonly Mock<IAdminKeylessUserResolver> _keylessUserResolver = new();

    private async Task<(AuthenticateResult Result, DefaultHttpContext HttpContext)> RunAsync(
        string path = "/api/v1/admin/tenants", string? userToken = "raw-user-token")
    {
        _tenantContext.SetupAllProperties();

        var handler = new AdminEndpointAuthenticationHandler(
            OptionsMonitorOf(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            _tenantContext.Object,
            _apiKeyService.Object,
            _adminRoleTenantResolver.Object,
            _keylessUserResolver.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        if (userToken != null)
        {
            httpContext.Request.Headers[AdminEndpointAuthenticationHandler.UserTokenHeaderName] = userToken;
        }

        var scheme = new AuthenticationScheme("AdminEndpointApiKeyScheme", null, typeof(AdminEndpointAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);

        var result = await handler.AuthenticateAsync();
        return (result, httpContext);
    }

    private static IOptionsMonitor<AuthenticationSchemeOptions> OptionsMonitorOf(AuthenticationSchemeOptions options)
    {
        var mock = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        mock.Setup(x => x.CurrentValue).Returns(options);
        mock.Setup(x => x.Get(It.IsAny<string?>())).Returns(options);
        return mock.Object;
    }

    [Fact]
    public async Task NonAdminApiPath_IsNotHandled()
    {
        var (result, _) = await RunAsync(path: "/api/v1/tenants");

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
        _keylessUserResolver.Verify(
            x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MissingUserTokenHeader_Fails_WithoutConsultingTheResolver()
    {
        var (result, httpContext) = await RunAsync(userToken: null);

        Assert.False(result.Succeeded);
        Assert.Equal("No access token found for AdminApi Endpoint connection",
            httpContext.Items[AdminEndpointAuthenticationHandler.FailureReasonItemKey]);
        _keylessUserResolver.Verify(
            x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EmptyUserTokenHeader_FailsTheSameAsMissing()
    {
        var (result, httpContext) = await RunAsync(userToken: "");

        Assert.False(result.Succeeded);
        Assert.Equal("No access token found for AdminApi Endpoint connection",
            httpContext.Items[AdminEndpointAuthenticationHandler.FailureReasonItemKey]);
    }

    [Fact]
    public async Task InvalidUserToken_Fails_WithTheResolversErrorMessage()
    {
        _keylessUserResolver
            .Setup(x => x.ResolveAsync("raw-user-token", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminKeylessResolutionResult(false, null, null, null, "Invalid user token"));

        var (result, httpContext) = await RunAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid user token", httpContext.Items[AdminEndpointAuthenticationHandler.FailureReasonItemKey]);
        _tenantContext.VerifySet(x => x.LoggedInUser = It.IsAny<string>(), Times.Never);
    }

    [Fact]
    public async Task NonExistentTenant_ForASysAdmin_PropagatesTenantNotFoundException_Not401()
    {
        _keylessUserResolver
            .Setup(x => x.ResolveAsync("raw-user-token", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TenantNotFoundException("ghost-tenant"));

        await Assert.ThrowsAsync<TenantNotFoundException>(() => RunAsync());
    }

    [Fact]
    public async Task ValidUserToken_Succeeds_AndPopulatesTenantContextFromTheResolution()
    {
        _keylessUserResolver
            .Setup(x => x.ResolveAsync("raw-user-token", It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminKeylessResolutionResult(true, UserId, TenantId, Roles, null));

        var (result, _) = await RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(UserId, _tenantContext.Object.LoggedInUser);
        Assert.Equal(TenantId, _tenantContext.Object.TenantId);
        Assert.Equal(UserType.UserToken, _tenantContext.Object.UserType);
        Assert.Equal(Roles, _tenantContext.Object.UserRoles);
        Assert.Equal(new[] { TenantId }, _tenantContext.Object.AuthorizedTenantIds);
        // No API key was presented, so nothing must be forwarded downstream as if one had been.
        Assert.Null(_tenantContext.Object.Authorization);
        Assert.Equal(UserId, result.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task ApiKeyHeaderPresent_TakesPriority_AndTheUserTokenHeaderIsNeverConsulted()
    {
        _tenantContext.SetupAllProperties();

        var handler = new AdminEndpointAuthenticationHandler(
            OptionsMonitorOf(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            _tenantContext.Object,
            _apiKeyService.Object,
            _adminRoleTenantResolver.Object,
            _keylessUserResolver.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/admin/tenants";
        httpContext.Request.Headers.Authorization = "Bearer sk-Xnai-not-a-real-key";
        httpContext.Request.Headers[AdminEndpointAuthenticationHandler.UserTokenHeaderName] = "raw-user-token";

        _apiKeyService.Setup(x => x.GetApiKeyByRawKeyAsync("sk-Xnai-not-a-real-key"))
            .ReturnsAsync((ApiKey?)null);

        var scheme = new AuthenticationScheme("AdminEndpointApiKeyScheme", null, typeof(AdminEndpointAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);
        await handler.AuthenticateAsync();

        // The API key branch ran (and failed on an unknown key) without the keyless branch ever
        // being reached, confirming a bearer token always wins when present.
        _keylessUserResolver.Verify(
            x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
