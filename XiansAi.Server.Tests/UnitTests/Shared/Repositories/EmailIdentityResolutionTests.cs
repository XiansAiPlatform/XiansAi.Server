using Microsoft.Extensions.Logging.Abstractions;
using Shared.Data.Models;
using Shared.Repositories;

namespace Tests.UnitTests.Shared.Repositories;

/// <summary>
/// One address can be held by several accounts — a record is keyed on the provider subject, so two
/// identity providers each holding an account for the same person is ordinary. A credential that
/// names only the address cannot say which of them it meant, so they are folded into one identity
/// rather than one being picked arbitrarily.
/// </summary>
public class EmailIdentityResolutionTests
{
    private const string Email = "person@example.com";
    private const string TenantId = "tenant-a";

    private static User Record(
        string userId,
        DateTime? createdAt = null,
        bool isSysAdmin = false,
        bool isLockedOut = false,
        params TenantRole[] tenantRoles) =>
        new()
        {
            UserId = userId,
            Email = Email,
            IsSysAdmin = isSysAdmin,
            IsLockedOut = isLockedOut,
            CreatedAt = createdAt ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TenantRoles = tenantRoles.ToList()
        };

    private static TenantRole Membership(string tenant, bool approved, params string[] roles) =>
        new() { Tenant = tenant, IsApproved = approved, Roles = roles.ToList() };

    private static EmailIdentityResolution? Fold(params User[] records) =>
        EmailIdentityResolution.From(Email, records, TenantId, NullLogger.Instance);

    [Fact]
    public void From_ReturnsNull_WhenNoRecordHoldsTheAddress()
    {
        Assert.Null(Fold());
    }

    // UsableRecords and ResolveSysAdmin are used directly by the participant lookup, which spans
    // every tenant rather than resolving one and so combines the records itself.

    [Fact]
    public void UsableRecords_DropsTheDisabledOnesAndOrdersTheRestByAge()
    {
        var older = Record("b-older", createdAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Record("a-newer", createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var disabled = Record("c-disabled", isLockedOut: true);

        var usable = EmailIdentityResolution.UsableRecords(new[] { newer, disabled, older });

        Assert.Equal(new[] { "b-older", "a-newer" }, usable.Select(record => record.UserId));
    }

    [Fact]
    public void ResolveSysAdmin_GrantsTheRoleOnlyWhenEveryRecordHoldsIt()
    {
        var admin = Record("admin", isSysAdmin: true);
        var other = Record("other", isSysAdmin: true);
        var notAnAdmin = Record("plain");

        Assert.True(EmailIdentityResolution.ResolveSysAdmin(
            Email, new[] { admin, other }, NullLogger.Instance));
        Assert.False(EmailIdentityResolution.ResolveSysAdmin(
            Email, new[] { admin, notAnAdmin }, NullLogger.Instance));
    }

    [Fact]
    public void From_ReturnsNull_WhenEveryRecordHoldingItIsLockedOut()
    {
        Assert.Null(Fold(Record("locked", isLockedOut: true)));
    }

    [Fact]
    public void From_TakesTheRolesOfTheOnlyRecordThatIsAMemberOfTheTenant()
    {
        var resolution = Fold(
            Record("a", tenantRoles: Membership(TenantId, approved: true, SystemRoles.TenantAdmin)),
            Record("b", tenantRoles: Membership("other-tenant", approved: true, SystemRoles.TenantAdmin)));

        Assert.Equal(new[] { SystemRoles.TenantAdmin }, resolution!.Roles);
        Assert.True(resolution.IsAmbiguous);
    }

    [Fact]
    public void From_UnionsTheRolesOfEveryRecordThatIsAMemberOfTheTenant()
    {
        var resolution = Fold(
            Record("a", tenantRoles: Membership(TenantId, approved: true, SystemRoles.TenantUser)),
            Record("b", tenantRoles: Membership(TenantId, approved: true, SystemRoles.TenantAdmin)));

        Assert.Equal(
            new[] { SystemRoles.TenantUser, SystemRoles.TenantAdmin },
            resolution!.Roles.OrderBy(r => r == SystemRoles.TenantAdmin));
    }

    [Fact]
    public void From_IgnoresAMembershipNobodyApproved()
    {
        // Reaching a tenant this way still requires one of its admins to have granted the
        // membership, so a row that is merely awaiting approval contributes nothing.
        var resolution = Fold(
            Record("a", tenantRoles: Membership(TenantId, approved: false, SystemRoles.TenantAdmin)));

        Assert.Empty(resolution!.Roles);
    }

    [Fact]
    public void From_ResolvesSysAdmin_ForAnAddressHeldByOneRecord()
    {
        var resolution = Fold(Record("a", isSysAdmin: true));

        Assert.True(resolution!.IsSysAdmin);
        Assert.Contains(SystemRoles.SysAdmin, resolution.Roles);
        Assert.False(resolution.IsAmbiguous);
    }

    [Fact]
    public void From_RefusesSysAdmin_WhenAnotherRecordSharesTheAddressWithoutTheRole()
    {
        // Guessing the administrator would let anyone able to register that address at a second
        // directory become one. A record that has not been granted the role is a record nobody has
        // accepted as the same person yet.
        var resolution = Fold(
            Record("admin", isSysAdmin: true),
            Record("someone-else"));

        Assert.False(resolution!.IsSysAdmin);
        Assert.DoesNotContain(SystemRoles.SysAdmin, resolution.Roles);
    }

    [Fact]
    public void From_ResolvesSysAdmin_WhenEveryRecordHoldingTheAddressHasTheRole()
    {
        // The same person holding an administrator account in two directories. Both were granted
        // the role deliberately, which is what makes the address safe to resolve from.
        var resolution = Fold(
            Record("admin-directory-one", isSysAdmin: true),
            Record("admin-directory-two", isSysAdmin: true));

        Assert.True(resolution!.IsSysAdmin);
        Assert.Contains(SystemRoles.SysAdmin, resolution.Roles);
        Assert.True(resolution.IsAmbiguous);
    }

    [Fact]
    public void From_StillResolvesSysAdmin_WhenTheOtherRecordIsLockedOut()
    {
        // A second account created for an administrator's address is created disabled, and disabled
        // records are dropped before the rest is decided. So merely registering that address at
        // another directory cannot take the role away from the account that holds it.
        var resolution = Fold(
            Record("admin", isSysAdmin: true),
            Record("awaiting-review", isLockedOut: true));

        Assert.True(resolution!.IsSysAdmin);
    }

    [Fact]
    public void From_PicksTheSamePrimaryAccount_WhicheverOrderTheRecordsArriveIn()
    {
        // The primary account is what the request acts as, and what its data is scoped to, so it
        // must not depend on the order a collection scan happened to return.
        var older = Record("zzz", createdAt: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Record("aaa", createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("zzz", Fold(older, newer)!.PrimaryUserId);
        Assert.Equal("zzz", Fold(newer, older)!.PrimaryUserId);
    }

    [Fact]
    public void From_BreaksATieOnCreationTimeWithTheUserId()
    {
        var sameMoment = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            "aaa",
            Fold(Record("zzz", createdAt: sameMoment), Record("aaa", createdAt: sameMoment))!.PrimaryUserId);
    }

    [Fact]
    public void From_ReportsEveryRecordThatContributed()
    {
        var resolution = Fold(
            Record("a", createdAt: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Record("b", createdAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Record("locked", isLockedOut: true));

        Assert.Equal(new[] { "a", "b" }, resolution!.CandidateUserIds);
    }
}
