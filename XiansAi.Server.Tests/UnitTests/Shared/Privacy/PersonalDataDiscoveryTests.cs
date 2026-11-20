using Features.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MongoDB.Bson;
using Shared.Data;
using Shared.Data.Models;
using Shared.Services.Privacy;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

/// <summary>
/// The registered <see cref="IPersonalDataSource"/>s are the registry of personal data. These tests check that the
/// registry is complete, that discovery reports it with scoped counts, and that the record of processing documents it.
/// </summary>
public class PersonalDataDiscoveryTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    /// <summary>Every personal and reference collection in the record of processing that discovery counts.</summary>
    private static readonly string[] PersonalDataCollections =
    [
        "conversation_thread", "conversation_message", "message_feedback", "contacts", "user_invitations", "documents",
        "logs", "activity_history", "usage_metrics", "audit_logs", "webhook_incomings", "webhook_deliveries", "users",
        "api_keys", "certificates", "secret_vault", "agents", "activations", "flow_definitions", "knowledge",
        "app_integrations", "tenants"
    ];

    private IPersonalDataDiscovery Discovery()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Privacy:Enabled"] = "true",
                ["Privacy:PseudonymKey"] = "discovery-tests-key-0123456789abcdef"
            })
            .Build();

        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(fixture.Database);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(databaseService.Object);
        services.AddPrivacyServices(configuration);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPersonalDataDiscovery>();
    }

    [Fact]
    public async Task Discovery_CoversEveryPersonalDataCollection()
    {
        var sources = await Discovery().DiscoverAsync(new PrivacySubject(), new PrivacyScope(), CancellationToken.None);

        Assert.Equal(
            PersonalDataCollections.Order(StringComparer.Ordinal),
            sources.Select(source => source.Collection).Distinct().Order(StringComparer.Ordinal));
        Assert.All(sources, source => Assert.Equal(0, source.Records));
    }

    [Fact]
    public async Task Discovery_AppliesTheTenantScope()
    {
        var inScope = TestPerson.NewTenant();
        var outOfScope = TestPerson.NewTenant();
        var person = TestPerson.New();
        foreach (var tenant in new[] { inScope, outOfScope })
        {
            await fixture.Database.GetCollection<BsonDocument>("contacts")
                .InsertOneAsync(new BsonDocument { ["tenant_id"] = tenant, ["email"] = person.Email });
        }

        var sources = await Discovery().DiscoverAsync(
            person.Subject(inScope), new PrivacyScope { TenantIds = [inScope] }, CancellationToken.None);

        Assert.Equal(1, sources.Single(source => source is { Source: "contacts", Collection: "contacts" }).Records);
    }

    [Fact]
    public async Task Discovery_AsksEverySourceInRegistrationOrder()
    {
        var discovery = new PersonalDataDiscovery(
        [
            new FakeSource("first", new PersonalDataCount("a", 1), new PersonalDataCount("b", 0)),
            new FakeSource("second", new PersonalDataCount("c", 3))
        ]);

        var sources = await discovery.DiscoverAsync(new PrivacySubject(), new PrivacyScope(), CancellationToken.None);

        Assert.Equal(
            [new DiscoveredPersonalData("first", "a", 1), new("first", "b", 0), new("second", "c", 3)],
            sources);
    }

    [Fact]
    public void DisabledPrivacy_RegistersNoSources()
    {
        var services = new ServiceCollection();
        services.AddPrivacyServices(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IPersonalDataSource));
    }

    [Fact]
    public void SourceNames_AreUnique()
    {
        var names = MongoPersonalDataSources.Areas.Select(area => area.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RecordOfProcessing_DocumentsEveryPersonalDataCollection()
    {
        var docs = File.ReadAllText(RecordOfProcessingPath());

        Assert.All(PersonalDataCollections, collection => Assert.Contains($"| `{collection}` |", docs));
    }

    private static string RecordOfProcessingPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "XiansAi.Server.Src", "docs", "PRIVACY_REQUESTS.md");
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("PRIVACY_REQUESTS.md was not found above the test output directory.");
    }

    private sealed class FakeSource(string name, params PersonalDataCount[] counts) : IPersonalDataSource
    {
        public string Name => name;

        public Task<IReadOnlyList<PersonalDataCount>> CountAsync(PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersonalDataCount>>(counts);
    }
}
