using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using Shared.Data;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Repositories;

/// <summary>
/// DB-level tests for the email lookups privacy requests use to find a person's contacts and invitations
/// from the emails <c>PrivacySubjectResolver</c> returns. Each test uses its own tenants, so tests stay
/// independent within the shared collections.
/// </summary>
public class PrivacyEmailLookupRepositoryTests : IClassFixture<MongoDbFixture>
{
    private readonly ContactRepository _contacts;
    private readonly InvitationRepository _invitations;

    public PrivacyEmailLookupRepositoryTests(MongoDbFixture fixture)
    {
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(fixture.Database);
        _contacts = new ContactRepository(databaseService.Object);
        _invitations = new InvitationRepository(databaseService.Object, NullLogger<InvitationRepository>.Instance);
    }

    private static string NewTenant() => $"tenant-{Guid.NewGuid():N}";

    private Task<Contact> AddContactAsync(string tenantId, string email) => _contacts.CreateAsync(new Contact
    {
        TenantId = tenantId,
        Name = email,
        Email = email,
        CreatedBy = "admin",
        CreatedAt = DateTime.UtcNow
    });

    private async Task<Invitation> AddInvitationAsync(string tenantId, string email, Status status = Status.Pending)
    {
        var invitation = new Invitation
        {
            TenantId = tenantId,
            Email = email,
            Token = Guid.NewGuid().ToString("N"),
            Status = status,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        await _invitations.CreateAsync(invitation);
        return invitation;
    }

    [Fact]
    public async Task Contacts_MatchIgnoringCase()
    {
        var tenant = NewTenant();
        await AddContactAsync(tenant, "Jane.Doe@Example.com");

        var found = await _contacts.FindByEmailsAsync([tenant], ["jane.doe@example.com"]);

        Assert.Equal("Jane.Doe@Example.com", Assert.Single(found).Email);
    }

    [Fact]
    public async Task Contacts_InTenantsOutsideTheScope_AreNotReturned()
    {
        var inScope = NewTenant();
        var outOfScope = NewTenant();
        await AddContactAsync(inScope, "jane@example.com");
        await AddContactAsync(outOfScope, "jane@example.com");

        var found = await _contacts.FindByEmailsAsync([inScope], ["jane@example.com"]);

        Assert.Equal(inScope, Assert.Single(found).TenantId);
    }

    [Fact]
    public async Task Contacts_AcrossSeveralTenantsAndEmails_AreAllReturned()
    {
        var first = NewTenant();
        var second = NewTenant();
        await AddContactAsync(first, "jane@example.com");
        await AddContactAsync(second, "jane.work@example.com");
        await AddContactAsync(second, "someone.else@example.com");

        var found = await _contacts.FindByEmailsAsync([first, second], ["jane@example.com", "jane.work@example.com"]);

        Assert.Equal(
            ["jane.work@example.com", "jane@example.com"],
            found.Select(c => c.Email).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Contacts_EmailIsMatchedExactly_NotAsAPattern()
    {
        var tenant = NewTenant();
        await AddContactAsync(tenant, "jaxe@example.com");
        await AddContactAsync(tenant, "jane@example.com.evil.org");
        await AddContactAsync(tenant, "xjane@example.com");

        var found = await _contacts.FindByEmailsAsync([tenant], ["ja.e@example.com", "jane@example.com"]);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Contacts_InputIsTrimmedAndBlankEmailsIgnored()
    {
        var tenant = NewTenant();
        await AddContactAsync(tenant, "jane@example.com");

        var found = await _contacts.FindByEmailsAsync([tenant], ["  JANE@example.com ", "", "   "]);

        Assert.Single(found);
    }

    [Fact]
    public async Task Contacts_NoTenantsOrNoEmails_ReturnsNothing()
    {
        var tenant = NewTenant();
        await AddContactAsync(tenant, "jane@example.com");

        Assert.Empty(await _contacts.FindByEmailsAsync([], ["jane@example.com"]));
        Assert.Empty(await _contacts.FindByEmailsAsync([tenant], []));
        Assert.Empty(await _contacts.FindByEmailsAsync([tenant], [" "]));
    }

    [Fact]
    public async Task Invitations_OfEveryStatus_AreReturned()
    {
        var tenant = NewTenant();
        await AddInvitationAsync(tenant, "jane@example.com", Status.Pending);
        await AddInvitationAsync(tenant, "jane@example.com", Status.Accepted);
        await AddInvitationAsync(tenant, "jane@example.com", Status.Expired);

        var found = await _invitations.FindByEmailsAsync([tenant], ["jane@example.com"]);

        Assert.Equal(
            [Status.Pending, Status.Accepted, Status.Expired],
            found.Select(i => i.Status).Order());
    }

    [Fact]
    public async Task Invitations_MatchIgnoringCase()
    {
        var tenant = NewTenant();
        await AddInvitationAsync(tenant, "JANE@Example.COM");

        var found = await _invitations.FindByEmailsAsync([tenant], ["jane@example.com"]);

        Assert.Single(found);
    }

    [Fact]
    public async Task Invitations_InTenantsOutsideTheScopeOrForOtherEmails_AreNotReturned()
    {
        var inScope = NewTenant();
        var outOfScope = NewTenant();
        await AddInvitationAsync(inScope, "jane@example.com");
        await AddInvitationAsync(inScope, "john@example.com");
        await AddInvitationAsync(outOfScope, "jane@example.com");

        var found = await _invitations.FindByEmailsAsync([inScope], ["jane@example.com"]);

        var invitation = Assert.Single(found);
        Assert.Equal(inScope, invitation.TenantId);
        Assert.Equal("jane@example.com", invitation.Email);
    }

    [Fact]
    public async Task Invitations_NoTenantsOrNoEmails_ReturnsNothing()
    {
        var tenant = NewTenant();
        await AddInvitationAsync(tenant, "jane@example.com");

        Assert.Empty(await _invitations.FindByEmailsAsync([], ["jane@example.com"]));
        Assert.Empty(await _invitations.FindByEmailsAsync([tenant], []));
    }
}
