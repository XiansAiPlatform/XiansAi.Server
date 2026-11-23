using Moq;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Services.Privacy;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

/// <summary>A person with a provider user ID and an email, unique to one test.</summary>
public sealed record TestPerson(string UserId, string Email)
{
    public static TestPerson New()
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        return new TestPerson($"auth0|{id}", $"{id}@example.com");
    }

    public string ProviderSubject => UserId[(UserId.IndexOf('|') + 1)..];

    public static string NewTenant() => $"tenant-{Guid.NewGuid():N}";

    public string WorkflowId(string tenantId) => $"{tenantId}:{ProviderSubject}:chat";

    /// <summary>The subject the resolver would return for this person within <paramref name="tenantIds"/>.</summary>
    public PrivacySubject Subject(params string[] tenantIds) => new()
    {
        UserIds = [UserId],
        Emails = [Email],
        ParticipantIds = new[] { UserId.ToLowerInvariant(), ProviderSubject, Email }.Order(StringComparer.Ordinal).ToList(),
        TenantIds = tenantIds.ToList(),
        MembershipTenantIds = tenantIds.ToList(),
        WorkflowIds = tenantIds.Select(WorkflowId).ToList()
    };
}

/// <summary>
/// DB-level tests for the MongoDB personal data sources. Every data area is seeded with one record of the person
/// per collection, then checked for correct counts, other tenants and people, and empty subjects. Each test uses
/// its own tenants and people, so tests stay independent within the shared collections.
/// </summary>
public class MongoPersonalDataSourcesTests : IClassFixture<MongoDbFixture>
{
    private readonly IMongoDatabase _database;

    public MongoPersonalDataSourcesTests(MongoDbFixture fixture)
    {
        _database = fixture.Database;
    }

    /// <summary>Platform-wide collections with no tenant field, so only all-tenant requests reach them.</summary>
    private static readonly HashSet<string> PlatformCollections = ["admin_capability_matrix"];

    /// <summary>Stores one record of the person in every collection of an area.</summary>
    private static readonly Dictionary<string, Func<IMongoDatabase, TestPerson, string, Task>> Seeds = new()
    {
        ["conversations"] = async (db, person, tenant) => await SeedThreadAsync(db, person, tenant, person.Email),
        ["contacts"] = (db, person, tenant) => Insert(db, "contacts", new()
        {
            // Stored as entered, so in a different case from the subject's lowercase email.
            ["tenant_id"] = tenant, ["email"] = person.Email.ToUpperInvariant(), ["created_by"] = "admin"
        }),
        ["invitations"] = (db, person, tenant) => Insert(db, "user_invitations", new()
        {
            ["tenant_id"] = tenant, ["email"] = person.Email, ["status"] = "Accepted"
        }),
        ["documents"] = (db, person, tenant) => Insert(db, "documents", new()
        {
            ["tenant_id"] = tenant, ["participant_id"] = person.Email, ["created_by"] = "agent"
        }),
        ["activity-and-logs"] = async (db, person, tenant) =>
        {
            await Insert(db, "logs", new() { ["tenant_id"] = tenant, ["participant_id"] = person.Email });
            await Insert(db, "activity_history", new() { ["workflow_id"] = person.WorkflowId(tenant) });
        },
        ["usage"] = (db, person, tenant) => Insert(db, "usage_metrics", new()
        {
            ["tenant_id"] = tenant, ["participant_id"] = person.Email, ["value"] = 120
        }),
        ["audit"] = (db, person, tenant) => Insert(db, "audit_logs", new()
        {
            ["tenant_id"] = tenant, ["participant_id"] = person.Email, ["logged_in_user"] = person.UserId
        }),
        ["webhooks"] = async (db, person, tenant) =>
        {
            await Insert(db, "webhook_incomings", new() { ["tenant_id"] = tenant, ["workflow_id"] = person.WorkflowId(tenant) });
            await Insert(db, "webhook_deliveries", new() { ["tenant_id"] = tenant, ["actor_user_id"] = person.UserId });
        },
        ["credentials"] = async (db, person, tenant) =>
        {
            await Insert(db, "api_keys", new() { ["tenant_id"] = tenant, ["participant_id"] = person.Email, ["created_by"] = "admin" });
            await Insert(db, "certificates", new() { ["TenantId"] = tenant, ["IssuedTo"] = person.UserId });
            await Insert(db, "secret_vault", new() { ["tenant_id"] = tenant, ["user_id"] = person.UserId });
        },
        ["ownership"] = async (db, person, tenant) =>
        {
            foreach (var collection in Area("ownership").Collections)
            {
                var document = new BsonDocument { ["created_by"] = person.UserId, ["updated_by"] = person.UserId };
                if (!PlatformCollections.Contains(collection.Name))
                {
                    document[collection.Name is "agents" or "flow_definitions" ? "tenant" : "tenant_id"] = tenant;
                }

                if (collection.Name == "tenants")
                {
                    document["domain"] = $"{tenant}.example.com";
                }

                await Insert(db, collection.Name, document);
            }
        },
        ["account"] = (db, person, tenant) => Insert(db, "users", new()
        {
            ["user_id"] = person.UserId,
            ["email"] = person.Email,
            ["tenant_roles"] = new BsonArray { new BsonDocument { ["tenant"] = tenant, ["roles"] = new BsonArray { "TenantUser" } } }
        })
    };

    public static TheoryData<string> AreaNames() => new(MongoPersonalDataSources.Areas.Select(area => area.Name));

    private static PersonalDataArea Area(string name) => MongoPersonalDataSources.Areas.Single(area => area.Name == name);

    private static Task Insert(IMongoDatabase database, string collection, BsonDocument document) =>
        database.GetCollection<BsonDocument>(collection).InsertOneAsync(document);

    private static async Task<string> SeedThreadAsync(IMongoDatabase database, TestPerson person, string tenant, string participantId)
    {
        var threadId = ObjectId.GenerateNewId();
        await Insert(database, "conversation_thread", new()
        {
            ["_id"] = threadId, ["tenant_id"] = tenant, ["workflow_id"] = person.WorkflowId(tenant), ["participant_id"] = participantId
        });
        await Insert(database, "conversation_message", new()
        {
            ["thread_id"] = threadId.ToString(), ["tenant_id"] = tenant, ["participant_id"] = participantId, ["text"] = "hello"
        });
        await Insert(database, "message_feedback", new()
        {
            ["thread_id"] = threadId.ToString(), ["tenant_id"] = tenant, ["participant_id"] = participantId
        });
        return threadId.ToString();
    }

    private IPersonalDataSource Source(string area)
    {
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(_database);
        return new MongoPersonalDataSource(Area(area), databaseService.Object);
    }

    private static PrivacyScope Scope(params string[] tenantIds) => new() { TenantIds = tenantIds.ToList() };

    private async Task<Dictionary<string, long>> CountAsync(string area, PrivacySubject subject, PrivacyScope scope) =>
        (await Source(area).CountAsync(subject, scope, CancellationToken.None))
            .ToDictionary(count => count.Collection, count => count.Records);

    [Fact]
    public void EveryArea_IsCoveredByTheseTests()
    {
        Assert.Equal(
            MongoPersonalDataSources.Areas.Select(area => area.Name).Order(StringComparer.Ordinal),
            Seeds.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AreaNames))]
    public async Task ThePersonsRecords_AreCountedInEveryCollection(string area)
    {
        var tenant = TestPerson.NewTenant();
        var person = TestPerson.New();
        await Seeds[area](_database, person, tenant);

        var counts = await CountAsync(area, person.Subject(tenant), Scope(tenant));
        var allTenants = await CountAsync(area, person.Subject(tenant), new() { TenantIds = [tenant], AllTenants = true });

        Assert.Equal(Area(area).Collections.Select(collection => collection.Name), counts.Keys);
        Assert.All(counts, count => Assert.Equal(PlatformCollections.Contains(count.Key) ? 0 : 1, count.Value));
        Assert.All(allTenants, count => Assert.Equal(1, count.Value));
    }

    [Theory]
    [MemberData(nameof(AreaNames))]
    public async Task OtherTenantsAndPeople_AreNotCounted(string area)
    {
        var inScope = TestPerson.NewTenant();
        var outOfScope = TestPerson.NewTenant();
        var person = TestPerson.New();
        await Seeds[area](_database, person, outOfScope);
        await Seeds[area](_database, TestPerson.New(), inScope);

        var counts = await CountAsync(area, person.Subject(inScope), Scope(inScope));

        Assert.All(counts, count => Assert.Equal(0, count.Value));
    }

    [Theory]
    [MemberData(nameof(AreaNames))]
    public async Task EmptySubjects_CountNothingButListEveryCollection(string area)
    {
        var tenant = TestPerson.NewTenant();
        var person = TestPerson.New();
        await Seeds[area](_database, person, tenant);
        var noIdentifiers = new PrivacySubject { TenantIds = [tenant], MembershipTenantIds = [tenant] };

        foreach (var (subject, scope) in new[] { (person.Subject(), Scope()), (noIdentifiers, Scope(tenant)) })
        {
            var counts = await CountAsync(area, subject, scope);

            Assert.Equal(Area(area).Collections.Count, counts.Count);
            Assert.All(counts, count => Assert.Equal(0, count.Value));
        }
    }

    [Fact]
    public async Task Messages_InThePersonsThreads_AreCountedEvenWithoutAParticipant()
    {
        var tenant = TestPerson.NewTenant();
        var person = TestPerson.New();
        var threadId = await SeedThreadAsync(_database, person, tenant, person.Email);
        await Insert(_database, "conversation_message", new()
        {
            ["thread_id"] = threadId, ["tenant_id"] = tenant, ["participant_id"] = BsonNull.Value, ["text"] = "agent reply"
        });

        var counts = await CountAsync("conversations", person.Subject(tenant), Scope(tenant));

        Assert.Equal(2, counts["conversation_message"]);
    }

    [Fact]
    public async Task WorkflowsSharedWithOtherParticipants_AreNotCountedThroughTheWorkflow()
    {
        var tenant = TestPerson.NewTenant();
        var person = TestPerson.New();
        await SeedThreadAsync(_database, person, tenant, person.Email);
        await SeedThreadAsync(_database, person, tenant, TestPerson.New().Email); // same workflow, another participant
        await Insert(_database, "activity_history", new() { ["workflow_id"] = person.WorkflowId(tenant) });

        var counts = await CountAsync("activity-and-logs", person.Subject(tenant), Scope(tenant));

        Assert.Equal(0, counts["activity_history"]);
    }

    [Fact]
    public async Task Accounts_AreCountedForATenantScopeOnlyWithAMembershipInIt()
    {
        var tenant = TestPerson.NewTenant();
        var elsewhere = TestPerson.NewTenant();
        var person = TestPerson.New();
        await Seeds["account"](_database, person, elsewhere);

        var tenantScoped = await CountAsync("account", person.Subject(tenant), Scope(tenant));
        var allTenants = await CountAsync("account", person.Subject(tenant), new() { TenantIds = [tenant], AllTenants = true });

        Assert.Equal(0, tenantScoped["users"]);
        Assert.Equal(1, allTenants["users"]);
    }

    [Fact]
    public async Task HealthChecks_AreFoundThroughThePersonsContacts()
    {
        var tenant = TestPerson.NewTenant();
        var elsewhere = TestPerson.NewTenant();
        var person = TestPerson.New();
        foreach (var contactTenant in new[] { tenant, elsewhere })
        {
            var contactId = ObjectId.GenerateNewId();
            await Insert(_database, "contacts", new() { ["_id"] = contactId, ["tenant_id"] = contactTenant, ["email"] = person.Email });
            await Insert(_database, "agent_health_checks", new()
            {
                ["tenant_id"] = contactTenant, ["created_by"] = "admin", ["notify_contact_ids"] = new BsonArray { "other", contactId.ToString() }
            });
        }

        var counts = await CountAsync("ownership", person.Subject(tenant), Scope(tenant));

        Assert.Equal(1, counts["agent_health_checks"]);
    }
}
