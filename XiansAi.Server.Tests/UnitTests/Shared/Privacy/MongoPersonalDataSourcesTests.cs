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
/// per collection, then checked for correct counts, tenant scope, other people and empty subjects. Each test uses
/// its own tenants and people, so tests stay independent within the shared collections.
/// </summary>
public class MongoPersonalDataSourcesTests : IClassFixture<MongoDbFixture>
{
    private readonly IMongoDatabase _database;

    public MongoPersonalDataSourcesTests(MongoDbFixture fixture)
    {
        _database = fixture.Database;
    }

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
                var tenantField = collection.Name is "agents" or "flow_definitions" ? "tenant" : "tenant_id";
                var document = new BsonDocument { [tenantField] = tenant, ["created_by"] = person.UserId };
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

        Assert.Equal(Area(area).Collections.Select(collection => collection.Name), counts.Keys);
        Assert.All(counts, count => Assert.Equal(1, count.Value));
    }

    [Theory]
    [MemberData(nameof(AreaNames))]
    public async Task TenantsOutsideTheScope_AreNotCounted(string area)
    {
        var inScope = TestPerson.NewTenant();
        var outOfScope = TestPerson.NewTenant();
        var person = TestPerson.New();
        await Seeds[area](_database, person, outOfScope);

        var counts = await CountAsync(area, person.Subject(inScope), Scope(inScope));

        Assert.All(counts, count => Assert.Equal(0, count.Value));
    }

    [Theory]
    [MemberData(nameof(AreaNames))]
    public async Task OtherPeople_AreNotCounted(string area)
    {
        var tenant = TestPerson.NewTenant();
        await Seeds[area](_database, TestPerson.New(), tenant);

        var counts = await CountAsync(area, TestPerson.New().Subject(tenant), Scope(tenant));

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
    public async Task Invitations_OfEveryStatus_AreCounted()
    {
        var tenant = TestPerson.NewTenant();
        var person = TestPerson.New();
        foreach (var status in new[] { "Pending", "Accepted", "Expired" })
        {
            await Insert(_database, "user_invitations", new() { ["tenant_id"] = tenant, ["email"] = person.Email, ["status"] = status });
        }

        var counts = await CountAsync("invitations", person.Subject(tenant), Scope(tenant));

        Assert.Equal(3, counts["user_invitations"]);
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
    public async Task RecordsWithNoTenant_AreCountedOnlyForAllTenantRequests()
    {
        var tenant = TestPerson.NewTenant();
        var person = TestPerson.New();
        await Insert(_database, "secret_vault", new() { ["tenant_id"] = BsonNull.Value, ["user_id"] = person.UserId });

        var tenantScoped = await CountAsync("credentials", person.Subject(tenant), Scope(tenant));
        var allTenants = await CountAsync("credentials", person.Subject(tenant), new() { TenantIds = [tenant], AllTenants = true });

        Assert.Equal(0, tenantScoped["secret_vault"]);
        Assert.Equal(1, allTenants["secret_vault"]);
    }

    [Fact]
    public async Task Agents_AreFoundThroughAccessLists()
    {
        var tenant = TestPerson.NewTenant();
        var person = TestPerson.New();
        await Insert(_database, "agents", new()
        {
            ["tenant"] = tenant, ["created_by"] = "admin", ["read_access"] = new BsonArray { "admin", person.UserId }
        });

        var counts = await CountAsync("ownership", person.Subject(tenant), Scope(tenant));

        Assert.Equal(1, counts["agents"]);
    }
}
