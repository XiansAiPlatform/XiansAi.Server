using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Features.WebApi.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Providers.Auth;
using Shared.Services;

namespace XiansAi.Server.Tests.UnitTests.Features.WebApi.Auth;

public class AuthRequirementHandlerTests
{
    private const string Token = "test-token";
    private const string UserId = "user-1";
    private const string TenantId = "tenant-a";

    private readonly Mock<ITenantCacheService> _tenantCache = new();
    private readonly Mock<ITokenValidationCache> _tokenCache = new();
    private readonly Mock<IAuthProvider> _authProvider = new();
    private readonly Mock<IAuthProviderFactory> _authProviderFactory = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IUserTenantService> _userTenantService = new();
    private readonly AuthRequirementHandler _handler;

    public AuthRequirementHandlerTests()
    {
        _tenantContext.SetupAllProperties();

        // The first attempt is served from the token cache. The handler retries a failed attempt
        // with fresh provider validation; failing that keeps every case to a single tenant lookup.
        _tokenCache
            .Setup(c => c.GetValidation(Token))
            .ReturnsAsync((true, true, UserId, new[] { TenantId }));
        _authProvider.Setup(p => p.ValidateToken(Token)).ReturnsAsync((false, (string?)null));
        _authProviderFactory.Setup(f => f.GetProvider()).Returns(_authProvider.Object);

        _handler = new AuthRequirementHandler(
            NullLogger<AuthRequirementHandler>.Instance,
            _tenantContext.Object,
            _authProviderFactory.Object,
            _tokenCache.Object,
            _userTenantService.Object,
            _tenantCache.Object);
    }

    [Fact]
    public async Task EnabledTenant_Succeeds()
    {
        var httpContext = CreateHttpContext();
        _tenantCache
            .Setup(c => c.GetByTenantIdAsync(TenantId, httpContext.RequestAborted, false))
            .ReturnsAsync(CreateTenant(enabled: true));

        var context = await AuthorizeAsync(httpContext, AuthRequirementOptions.FullTenantValidation);

        Assert.True(context.HasSucceeded);
        Assert.Equal(TenantId, _tenantContext.Object.TenantId);
        _tenantCache.Verify(c => c.GetByTenantIdAsync(TenantId, httpContext.RequestAborted, false), Times.Once);
    }

    [Fact]
    public async Task DisabledTenant_Fails()
    {
        SetupTenantLookup().ReturnsAsync(CreateTenant(enabled: false));

        var context = await AuthorizeAsync(CreateHttpContext(), AuthRequirementOptions.FullTenantValidation);

        AssertFailedWith(context, $"Tenant {TenantId} is disabled");
    }

    [Fact]
    public async Task MissingTenant_Fails()
    {
        SetupTenantLookup().ReturnsAsync((Tenant?)null);

        var context = await AuthorizeAsync(CreateHttpContext(), AuthRequirementOptions.FullTenantValidation);

        AssertFailedWith(context, $"Tenant {TenantId} configuration not found");
    }

    [Fact]
    public async Task InvalidTenantId_FailsWithoutThrowing()
    {
        SetupTenantLookup().ThrowsAsync(new ValidationException("Invalid Tenant ID format"));

        var context = await AuthorizeAsync(CreateHttpContext(), AuthRequirementOptions.FullTenantValidation);

        AssertFailedWith(context, $"Tenant {TenantId} configuration not found");
    }

    [Fact]
    public async Task TenantLookupError_FailsWithoutThrowing()
    {
        SetupTenantLookup().ThrowsAsync(new InvalidOperationException("database unavailable"));

        var context = await AuthorizeAsync(CreateHttpContext(), AuthRequirementOptions.FullTenantValidation);

        AssertFailedWith(context, $"Tenant {TenantId} configuration not found");
    }

    [Fact]
    public async Task CancelledTenantLookup_Propagates()
    {
        SetupTenantLookup().ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => AuthorizeAsync(CreateHttpContext(), AuthRequirementOptions.FullTenantValidation));
    }

    [Fact]
    public async Task TenantWithoutConfig_SkipsTenantLookup()
    {
        var context = await AuthorizeAsync(CreateHttpContext(), AuthRequirementOptions.TenantWithoutConfig);

        Assert.True(context.HasSucceeded);
        _tenantCache.Verify(
            c => c.GetByTenantIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Never);
    }

    private Moq.Language.Flow.ISetup<ITenantCacheService, Task<Tenant?>> SetupTenantLookup() =>
        _tenantCache.Setup(c => c.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>(), false));

    private async Task<AuthorizationHandlerContext> AuthorizeAsync(HttpContext httpContext, AuthRequirementOptions options)
    {
        var requirement = new AuthRequirement(options);
        var context = new AuthorizationHandlerContext([requirement], httpContext.User, httpContext);
        await _handler.HandleAsync(context);
        return context;
    }

    private static void AssertFailedWith(AuthorizationHandlerContext context, string reason)
    {
        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
        Assert.Contains(context.FailureReasons, r => r.Message == reason);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity("Test"))
        };
        httpContext.Request.Headers.Authorization = $"Bearer {Token}";
        httpContext.Request.Headers["X-Tenant-Id"] = TenantId;
        return httpContext;
    }

    private static Tenant CreateTenant(bool enabled) => new()
    {
        Id = "tenant-doc-id",
        TenantId = TenantId,
        Name = "Tenant A",
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
        Enabled = enabled
    };
}
