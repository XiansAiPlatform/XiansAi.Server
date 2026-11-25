using Features.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Data;
using Shared.Data.Models;
using Shared.Services.Privacy;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

/// <summary>
/// The registered <see cref="IPersonalDataSource"/>s are the registry of personal data. These tests check that the
/// registry is complete, that it is registered only when privacy is enabled, and that the record of processing
/// documents it.
/// </summary>
public class PersonalDataDiscoveryTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    /// <summary>Every personal and reference collection in the record of processing that discovery counts.</summary>
    private static readonly string[] PersonalDataCollections =
    [
        "conversation_thread", "conversation_message", "message_feedback", "contacts", "user_invitations", "documents",
        "logs", "activity_history", "usage_metrics", "audit_logs", "webhook_incomings", "webhook_deliveries", "users",
        "api_keys", "certificates", "secret_vault", "agents", "activations", "flow_definitions", "knowledge",
        "app_integrations", "tenants", "tenant_oidc_config", "tenant_smtp_config", "tenant_temporal_config",
        "admin_capability_matrix", "agent_health_checks"
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
    public void DisabledPrivacy_RegistersNoSources()
    {
        var services = new ServiceCollection();
        services.AddPrivacyServices(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IPersonalDataSource));
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
            var path = Path.Join(directory.FullName, "XiansAi.Server.Src", "docs", "PRIVACY_REQUESTS.md");
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("PRIVACY_REQUESTS.md was not found above the test output directory.");
    }
}
