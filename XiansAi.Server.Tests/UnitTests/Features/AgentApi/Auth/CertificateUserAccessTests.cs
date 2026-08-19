using Features.AgentApi.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Data.Models;
using Shared.Repositories;

namespace Tests.UnitTests.Features.AgentApi.Auth;

/// <summary>
/// A certificate whose OU is a user id must apply the same rules as one whose OU is an email:
/// a locked-out account cannot authenticate, and only an approved membership of the
/// certificate's tenant contributes roles.
/// </summary>
public class CertificateUserAccessTests
{
    private const string TenantId = "tenant-a";
    private const string UserId = "user-1";

    private static User Account(
        bool lockedOut = false,
        bool isSysAdmin = false,
        params TenantRole[] tenantRoles) =>
        new()
        {
            UserId = UserId,
            Email = "user@example.com",
            IsLockedOut = lockedOut,
            IsSysAdmin = isSysAdmin,
            TenantRoles = tenantRoles.ToList()
        };

    private static TenantRole Membership(bool approved, params string[] roles) =>
        new() { Tenant = TenantId, IsApproved = approved, Roles = roles.ToList() };

    private static (string? Error, EmailIdentityResolution? Identity) Resolve(User user) =>
        CertificateUserAccess.Resolve(user, TenantId, NullLogger.Instance);

    [Fact]
    public void Resolve_RefusesALockedOutAccount()
    {
        var (error, identity) = Resolve(
            Account(lockedOut: true, tenantRoles: Membership(approved: true, SystemRoles.TenantAdmin)));

        Assert.Equal(CertificateUserAccess.LockedOutError, error);
        Assert.Null(identity);
    }

    [Fact]
    public void Resolve_DoesNotCountAnUnapprovedMembership()
    {
        var (error, identity) = Resolve(
            Account(tenantRoles: Membership(approved: false, SystemRoles.TenantAdmin)));

        Assert.Null(error);
        Assert.Empty(identity!.Roles);
        Assert.False(identity.IsSysAdmin);
    }

    [Fact]
    public void Resolve_TakesTheApprovedMembershipsRoles()
    {
        var (error, identity) = Resolve(
            Account(tenantRoles: Membership(approved: true, SystemRoles.TenantAdmin)));

        Assert.Null(error);
        Assert.Equal(new[] { SystemRoles.TenantAdmin }, identity!.Roles);
    }

    [Fact]
    public void Resolve_StillTreatsASysAdminAsSysAdmin_WithoutAnApprovedMembership()
    {
        // SysAdmin is global. Lacking a membership of this tenant does not strip it — the email
        // path behaves the same way.
        var (error, identity) = Resolve(Account(isSysAdmin: true));

        Assert.Null(error);
        Assert.True(identity!.IsSysAdmin);
        Assert.Contains(SystemRoles.SysAdmin, identity.Roles);
    }
}
