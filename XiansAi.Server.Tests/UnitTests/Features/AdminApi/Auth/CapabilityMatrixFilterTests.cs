using Features.AdminApi.Auth;
using Features.AdminApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Auth;

namespace Tests.UnitTests.Features.AdminApi.Auth;

/// <summary>
/// The safety properties of <see cref="CapabilityMatrixFilter"/>, in the order they matter:
/// SysAdmin can never be locked out by matrix data, a migrated route that declares nothing is denied
/// rather than opened, and an unmigrated route is untouched.
/// </summary>
public class CapabilityMatrixFilterTests
{
    private const string Action = CapabilityActions.TenantUsersDelete;

    private readonly Mock<ICapabilityMatrixService> _matrix = new();

    private static ITenantContext CallerWith(params string[] roles)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.UserRoles).Returns(roles);
        context.SetupGet(x => x.LoggedInUser).Returns("caller-1");
        return context.Object;
    }

    private void MatrixAllows(string action, params string[] roles) =>
        _matrix.Setup(x => x.GetAllowedRolesAsync(action)).ReturnsAsync(roles);

    private async Task<(object? Result, bool ReachedHandler)> InvokeAsync(
        ITenantContext tenantContext, params object[] endpointMetadata)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        services.AddSingleton(_matrix.Object);
        services.AddLogging();

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(endpointMetadata),
            "TestRoute"));

        var reached = false;
        var result = await new CapabilityMatrixFilter().InvokeAsync(
            EndpointFilterInvocationContext.Create(httpContext),
            _ =>
            {
                reached = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });

        return (result, reached);
    }

    private static void AssertForbidden(object? result) =>
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

    private static object[] Enforced(string? action) => action == null
        ? new object[] { CapabilityEnforcedMetadata.Instance }
        : new object[] { CapabilityEnforcedMetadata.Instance, new RequireCapabilityMetadata(action) };

    // ----- The invariant: no matrix state can lock SysAdmin out -----

    [Fact]
    public async Task SysAdmin_IsAllowed_WhenTheActionAllowsNobody()
    {
        MatrixAllows(Action);

        var (_, reached) = await InvokeAsync(CallerWith(SystemRoles.SysAdmin), Enforced(Action));

        Assert.True(reached);
        // The bypass runs ahead of the lookup, so a matrix that is unreadable, empty, or wrong cannot
        // affect a SysAdmin at all.
        _matrix.Verify(x => x.GetAllowedRolesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SysAdmin_IsAllowed_WhenARowExplicitlyExcludesSysAdmin()
    {
        MatrixAllows(Action, SystemRoles.TenantUser);

        var (_, reached) = await InvokeAsync(CallerWith(SystemRoles.SysAdmin), Enforced(Action));

        Assert.True(reached);
    }

    [Fact]
    public async Task SysAdmin_IsAllowed_OnAnEnforcedRouteThatDeclaresNoAction()
    {
        var (_, reached) = await InvokeAsync(CallerWith(SystemRoles.SysAdmin), Enforced(null));

        Assert.True(reached);
    }

    // ----- Ordinary matrix resolution -----

    [Fact]
    public async Task TenantAdmin_IsAllowed_WhenTheActionGrantsTheirRole()
    {
        MatrixAllows(Action, SystemRoles.TenantAdmin);

        var (_, reached) = await InvokeAsync(CallerWith(SystemRoles.TenantAdmin), Enforced(Action));

        Assert.True(reached);
    }

    [Fact]
    public async Task TenantAdmin_IsDenied_WhenAnOverrideReplacesTheDefaultWithNothing()
    {
        MatrixAllows(Action);

        var (result, reached) = await InvokeAsync(CallerWith(SystemRoles.TenantAdmin), Enforced(Action));

        Assert.False(reached);
        AssertForbidden(result);
    }

    [Fact]
    public async Task TenantAdmin_IsDenied_WhenTheActionGrantsSomeOtherRole()
    {
        MatrixAllows(Action, SystemRoles.TenantUser);

        var (result, reached) = await InvokeAsync(CallerWith(SystemRoles.TenantAdmin), Enforced(Action));

        Assert.False(reached);
        AssertForbidden(result);
    }

    [Fact]
    public async Task CallerWithNoRolesAtAll_IsDenied()
    {
        MatrixAllows(Action, SystemRoles.TenantAdmin);

        var (result, reached) = await InvokeAsync(CallerWith(), Enforced(Action));

        Assert.False(reached);
        AssertForbidden(result);
    }

    // ----- Unmigrated groups stay untouched -----

    [Fact]
    public async Task RouteWithNoCapabilityMetadata_PassesThrough_WithoutConsultingTheMatrix()
    {
        var (_, reached) = await InvokeAsync(CallerWith(SystemRoles.TenantAdmin));

        Assert.True(reached);
        _matrix.Verify(x => x.GetAllowedRolesAsync(It.IsAny<string>()), Times.Never);
    }

    // ----- Forgetting RequireCapability inside a migrated group fails closed -----

    [Fact]
    public async Task EnforcedRouteThatDeclaresNoAction_DeniesANonSysAdmin()
    {
        var (result, reached) = await InvokeAsync(CallerWith(SystemRoles.TenantAdmin), Enforced(null));

        Assert.False(reached);
        AssertForbidden(result);
        // Nothing was looked up: there was no action to look up, which is the configuration error.
        _matrix.Verify(x => x.GetAllowedRolesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RouteLevelAction_OverridesAGroupLevelOne()
    {
        // Group metadata is added before route metadata and GetMetadata<T>() returns the last match,
        // so a group-wide default can be declared and then overridden per route.
        MatrixAllows(CapabilityActions.TenantUsersList, SystemRoles.TenantAdmin);
        MatrixAllows(CapabilityActions.TenantUsersDelete);

        var (result, reached) = await InvokeAsync(
            CallerWith(SystemRoles.TenantAdmin),
            CapabilityEnforcedMetadata.Instance,
            new RequireCapabilityMetadata(CapabilityActions.TenantUsersList),
            new RequireCapabilityMetadata(CapabilityActions.TenantUsersDelete));

        Assert.False(reached);
        AssertForbidden(result);
        _matrix.Verify(x => x.GetAllowedRolesAsync(CapabilityActions.TenantUsersDelete), Times.Once);
        _matrix.Verify(x => x.GetAllowedRolesAsync(CapabilityActions.TenantUsersList), Times.Never);
    }
}
