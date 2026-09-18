using Features.AdminApi.Auth;
using Features.AdminApi.Models;
using Features.AdminApi.Repositories;
using Features.AdminApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Services;
using Shared.Utils;
using StatusCode = Shared.Utils.Services.StatusCode;

namespace Tests.UnitTests.Features.AdminApi.Services;

/// <summary>
/// Resolution rules for the capability matrix: a stored row replaces the code default rather than
/// adding to it, a missing row falls back to the default rather than to deny, and a non-delegable
/// action ignores storage entirely.
/// </summary>
public class CapabilityMatrixServiceTests
{
    private const string CacheKey = "admin_capability_matrix:all";

    private readonly Mock<ICapabilityMatrixRepository> _repository = new();
    private readonly Mock<ICacheProvider> _cacheProvider = new();
    private readonly Mock<IWebhookEventPublisher> _webhooks = new();

    public CapabilityMatrixServiceTests()
    {
        // Default: a cold cache that accepts whatever is written to it.
        _cacheProvider
            .Setup(x => x.GetAsync<List<CapabilityMatrixEntry>>(It.IsAny<string>()))
            .ReturnsAsync((List<CapabilityMatrixEntry>?)null);
        _cacheProvider
            .Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<List<CapabilityMatrixEntry>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        _cacheProvider.Setup(x => x.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);
        _repository.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<CapabilityMatrixEntry>());
        _repository.Setup(x => x.UpsertAsync(It.IsAny<CapabilityMatrixEntry>()))
            .ReturnsAsync((CapabilityMatrixEntry?)null);
        _repository.Setup(x => x.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync((CapabilityMatrixEntry?)null);
    }

    private CapabilityMatrixService BuildService() =>
        new(_repository.Object,
            new ObjectCache(_cacheProvider.Object, NullLogger<ObjectCache>.Instance),
            _webhooks.Object,
            NullLogger<CapabilityMatrixService>.Instance);

    private void StoredRows(params CapabilityMatrixEntry[] rows) =>
        _repository.Setup(x => x.GetAllAsync()).ReturnsAsync(rows.ToList());

    private static CapabilityMatrixEntry Row(string action, params string[] roles) => new()
    {
        Action = action,
        AllowedRoles = roles.ToList(),
    };

    // ----- Resolution -----

    [Fact]
    public async Task GetAllowedRoles_FallsBackToTheCodeDefault_WhenNoRowExists()
    {
        var roles = await BuildService().GetAllowedRolesAsync(CapabilityActions.TenantUsersDelete);

        Assert.Equal(new[] { SystemRoles.TenantAdmin }, roles);
    }

    [Fact]
    public async Task GetAllowedRoles_IsEmptyByDefault_ForGlobalUserActions()
    {
        var roles = await BuildService().GetAllowedRolesAsync(CapabilityActions.GlobalUsersList);

        Assert.Empty(roles);
    }

    [Fact]
    public async Task GetAllowedRoles_UsesTheRowInsteadOfTheDefault_NotTheUnionOfBoth()
    {
        StoredRows(Row(CapabilityActions.TenantUsersDelete, SystemRoles.TenantUser));

        var roles = await BuildService().GetAllowedRolesAsync(CapabilityActions.TenantUsersDelete);

        // Replacing rather than unioning is what lets a default be tightened without a redeploy.
        Assert.Equal(new[] { SystemRoles.TenantUser }, roles);
        Assert.DoesNotContain(SystemRoles.TenantAdmin, roles);
    }

    [Fact]
    public async Task GetAllowedRoles_CanTightenADefaultToNobody()
    {
        StoredRows(Row(CapabilityActions.TenantUsersDelete));

        Assert.Empty(await BuildService().GetAllowedRolesAsync(CapabilityActions.TenantUsersDelete));
    }

    [Fact]
    public async Task GetAllowedRoles_IsEmpty_ForAnActionNothingDeclares()
    {
        Assert.Empty(await BuildService().GetAllowedRolesAsync("some.action.nobody.declared"));
    }

    [Fact]
    public async Task GetAllowedRoles_IgnoresAStoredRow_ForANonDelegableAction()
    {
        StoredRows(Row(CapabilityActions.GlobalUsersSysAdminSet, SystemRoles.TenantAdmin));

        var roles = await BuildService().GetAllowedRolesAsync(CapabilityActions.GlobalUsersSysAdminSet);

        Assert.Empty(roles);
        _repository.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task GetAllowedRoles_IgnoresAStoredRow_ForANonDelegableActionWithANonEmptyDefault()
    {
        // NonDelegable doesn't only mean SysAdmin-only: TenantAgentAccessAccess fixes a non-empty
        // [TenantAdmin] default that a stored row must not be able to widen, narrow, or replace either.
        StoredRows(Row(CapabilityActions.TenantAgentAccessAccess, SystemRoles.TenantUser));

        var roles = await BuildService().GetAllowedRolesAsync(CapabilityActions.TenantAgentAccessAccess);

        Assert.Equal(new[] { SystemRoles.TenantAdmin }, roles);
        _repository.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public void EveryDeclaredAction_CarriesItsOwnDefault()
    {
        // The single catalog table is what makes this true by construction rather than by convention:
        // there is no way to declare an action and forget its default in another collection.
        Assert.All(CapabilityActions.All, action =>
        {
            Assert.NotNull(action.DefaultRoles);
            Assert.False(string.IsNullOrWhiteSpace(action.Description));
            Assert.DoesNotContain(SystemRoles.SysAdmin, action.DefaultRoles);
        });

        Assert.Equal(CapabilityActions.All.Count, CapabilityActions.All.Select(a => a.Name).Distinct().Count());
    }

    // ----- Caching -----

    [Fact]
    public async Task GetAllowedRoles_ReadsThroughAndPopulatesTheCache_OnAMiss()
    {
        StoredRows(Row(CapabilityActions.TenantUsersDelete, SystemRoles.TenantUser));

        await BuildService().GetAllowedRolesAsync(CapabilityActions.TenantUsersDelete);

        _repository.Verify(x => x.GetAllAsync(), Times.Once);
        _cacheProvider.Verify(
            x => x.SetAsync(CacheKey, It.IsAny<List<CapabilityMatrixEntry>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllowedRoles_SkipsTheDatabase_OnACacheHit()
    {
        _cacheProvider
            .Setup(x => x.GetAsync<List<CapabilityMatrixEntry>>(CacheKey))
            .ReturnsAsync(new List<CapabilityMatrixEntry> { Row(CapabilityActions.TenantUsersDelete, SystemRoles.TenantUser) });

        var roles = await BuildService().GetAllowedRolesAsync(CapabilityActions.TenantUsersDelete);

        Assert.Equal(new[] { SystemRoles.TenantUser }, roles);
        _repository.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task Upsert_InvalidatesTheCache()
    {
        await BuildService().UpsertAsync(
            CapabilityActions.TenantUsersDelete, new List<string> { SystemRoles.TenantUser }, null, "actor");

        _cacheProvider.Verify(x => x.RemoveAsync(CacheKey), Times.Once);
    }

    [Fact]
    public async Task Delete_InvalidatesTheCache()
    {
        _repository.Setup(x => x.DeleteAsync(CapabilityActions.TenantUsersDelete))
            .ReturnsAsync(Row(CapabilityActions.TenantUsersDelete, SystemRoles.TenantUser));

        await BuildService().DeleteAsync(CapabilityActions.TenantUsersDelete, "actor");

        _cacheProvider.Verify(x => x.RemoveAsync(CacheKey), Times.Once);
    }

    // ----- Writes -----

    [Fact]
    public async Task Upsert_WritesTheAuditFields_AndNeverSendsAnId()
    {
        CapabilityMatrixEntry? saved = null;
        _repository.Setup(x => x.UpsertAsync(It.IsAny<CapabilityMatrixEntry>()))
            .Callback<CapabilityMatrixEntry>(e => saved = e)
            .ReturnsAsync((CapabilityMatrixEntry?)null);

        await BuildService().UpsertAsync(
            CapabilityActions.TenantUsersDelete, new List<string> { SystemRoles.TenantUser }, "why", "actor");

        Assert.NotNull(saved);
        // The repository writes fields, never a whole document, so the service has no id to invent —
        // and Mongo rejects a replacement that alters an existing _id.
        Assert.Null(saved!.Id);
        Assert.Equal("why", saved.Description);
        Assert.Equal("actor", saved.UpdatedBy);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task Upsert_PublishesThePreviousRoles_FromTheWriteItself()
    {
        // The previous row comes back from the atomic write, so the audit trail needs no extra read.
        _repository.Setup(x => x.UpsertAsync(It.IsAny<CapabilityMatrixEntry>()))
            .ReturnsAsync(Row(CapabilityActions.TenantUsersDelete, SystemRoles.TenantUser));

        await BuildService().UpsertAsync(
            CapabilityActions.TenantUsersDelete, new List<string> { SystemRoles.TenantAdmin }, null, "actor");

        _repository.Verify(x => x.GetAllAsync(), Times.Never);
        _webhooks.Verify(
            x => x.PublishAsync(DomainEventTypes.CapabilityMatrixUpdated, It.IsAny<object>(), null),
            Times.Once);
    }

    [Fact]
    public async Task Upsert_SavesAnUnrecognizedRoleString()
    {
        CapabilityMatrixEntry? saved = null;
        _repository.Setup(x => x.UpsertAsync(It.IsAny<CapabilityMatrixEntry>()))
            .Callback<CapabilityMatrixEntry>(e => saved = e)
            .ReturnsAsync((CapabilityMatrixEntry?)null);

        // Warned about, not rejected: the role set is open so a future role needs no redeploy.
        var result = await BuildService().UpsertAsync(
            CapabilityActions.TenantUsersDelete, new List<string> { "FutureReadOnlyRole" }, null, "actor");

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "FutureReadOnlyRole" }, saved!.AllowedRoles);
    }

    [Fact]
    public async Task Upsert_RejectsAnUnknownAction()
    {
        var result = await BuildService().UpsertAsync(
            "some.action.nobody.declared", new List<string>(), null, "actor");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _repository.Verify(x => x.UpsertAsync(It.IsAny<CapabilityMatrixEntry>()), Times.Never);
    }

    [Fact]
    public async Task Upsert_RejectsANonDelegableAction()
    {
        // One PUT granting this to TenantAdmin would be a path out of the tenant boundary.
        var result = await BuildService().UpsertAsync(
            CapabilityActions.GlobalUsersSysAdminSet, new List<string> { SystemRoles.TenantAdmin }, null, "actor");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _repository.Verify(x => x.UpsertAsync(It.IsAny<CapabilityMatrixEntry>()), Times.Never);
    }

    [Fact]
    public async Task Delete_RevertsTheActionToItsCodeDefault()
    {
        StoredRows(Row(CapabilityActions.TenantUsersDelete));
        _repository.Setup(x => x.DeleteAsync(CapabilityActions.TenantUsersDelete))
            .ReturnsAsync(Row(CapabilityActions.TenantUsersDelete));

        var service = BuildService();
        Assert.Empty(await service.GetAllowedRolesAsync(CapabilityActions.TenantUsersDelete));

        Assert.True((await service.DeleteAsync(CapabilityActions.TenantUsersDelete, "actor")).IsSuccess);

        StoredRows();
        Assert.Equal(
            new[] { SystemRoles.TenantAdmin },
            await BuildService().GetAllowedRolesAsync(CapabilityActions.TenantUsersDelete));
    }

    [Fact]
    public async Task Delete_ReportsNotFound_WhenNoRowIsStored()
    {
        _repository.Setup(x => x.DeleteAsync(CapabilityActions.TenantUsersDelete))
            .ReturnsAsync((CapabilityMatrixEntry?)null);

        var result = await BuildService().DeleteAsync(CapabilityActions.TenantUsersDelete, "actor");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    // ----- Effective view -----

    [Fact]
    public async Task GetEffective_ReportsSysAdminInEffectiveRolesButNotTheStoredRule()
    {
        var effective = await BuildService().GetEffectiveAsync();

        Assert.Equal(CapabilityActions.All.Count, effective.Count);

        var delete = effective.Single(e => e.Action == CapabilityActions.TenantUsersDelete);
        Assert.Equal("default", delete.Source);
        Assert.Equal(new[] { SystemRoles.TenantAdmin }, delete.AllowedRoles);
        // SysAdmin is absent from the stored rule by design, so the view says so rather than leaving
        // a reader to conclude SysAdmin is excluded.
        Assert.DoesNotContain(SystemRoles.SysAdmin, delete.AllowedRoles);
        Assert.Contains(SystemRoles.SysAdmin, delete.EffectiveRoles);
    }

    [Fact]
    public async Task GetEffective_MarksAStoredRowAsAnOverride()
    {
        StoredRows(Row(CapabilityActions.TenantUsersDelete, SystemRoles.TenantUser));

        var delete = (await BuildService().GetEffectiveAsync())
            .Single(e => e.Action == CapabilityActions.TenantUsersDelete);

        Assert.Equal("override", delete.Source);
        Assert.Equal(new[] { SystemRoles.TenantUser }, delete.AllowedRoles);
    }

    [Fact]
    public async Task GetEffective_ReportsANonDelegableActionAsDefault_EvenWithAnInertRowStored()
    {
        StoredRows(Row(CapabilityActions.GlobalUsersSysAdminSet, SystemRoles.TenantAdmin));

        var entry = (await BuildService().GetEffectiveAsync())
            .Single(e => e.Action == CapabilityActions.GlobalUsersSysAdminSet);

        Assert.True(entry.NonDelegable);
        Assert.Empty(entry.AllowedRoles);
        Assert.Equal("default", entry.Source);
    }

    [Fact]
    public async Task GetEffective_ReportsANonDelegableActionsFixedDefault_EvenWithAnInertRowStored()
    {
        StoredRows(Row(CapabilityActions.TenantTemplatesAccess, SystemRoles.TenantUser));

        var entry = (await BuildService().GetEffectiveAsync())
            .Single(e => e.Action == CapabilityActions.TenantTemplatesAccess);

        Assert.True(entry.NonDelegable);
        Assert.Equal(new[] { SystemRoles.TenantAdmin }, entry.AllowedRoles);
        Assert.Equal("default", entry.Source);
    }
}
