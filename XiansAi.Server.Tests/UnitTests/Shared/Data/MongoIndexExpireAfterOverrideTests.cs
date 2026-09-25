using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Data;

namespace Tests.UnitTests.Shared.Data;

public class MongoIndexExpireAfterOverrideTests
{
    private const string OverrideKey = "MongoIndexes:logs:logs_ttl_created_at:ExpireAfter";

    private static Dictionary<string, List<MongoIndexDefinition>> Definitions() => new()
    {
        ["logs"] =
        [
            new MongoIndexDefinition
            {
                Name = "logs_ttl_created_at",
                Keys = new Dictionary<string, string> { ["created_at"] = "asc" },
                Background = true,
                ExpireAfter = TimeSpan.FromDays(15)
            },
            new MongoIndexDefinition
            {
                Name = "workflow_run_id_1_autocreated",
                Keys = new Dictionary<string, string> { ["workflow_run_id"] = "asc" }
            }
        ],
        ["usage_metrics"] =
        [
            new MongoIndexDefinition
            {
                Name = "usage_metrics_ttl",
                Keys = new Dictionary<string, string> { ["created_at"] = "asc" },
                ExpireAfter = TimeSpan.FromDays(90)
            }
        ]
    };

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static MongoIndexDefinition Find(
        Dictionary<string, List<MongoIndexDefinition>> definitions, string collection, string name) =>
        definitions[collection].Single(d => d.Name == name);

    private static Dictionary<string, List<MongoIndexDefinition>> Apply(IConfiguration configuration) =>
        MongoIndexSynchronizer.ApplyExpireAfterOverrides(Definitions(), configuration, new Mock<ILogger>().Object);

    [Fact]
    public void NoOverride_LeavesYamlValues()
    {
        var result = Apply(Config());

        Assert.Equal(TimeSpan.FromDays(15), Find(result, "logs", "logs_ttl_created_at").ExpireAfter);
        Assert.Equal(TimeSpan.FromDays(90), Find(result, "usage_metrics", "usage_metrics_ttl").ExpireAfter);
    }

    [Fact]
    public void ValidOverride_ReplacesOnlyTheTargetedIndex()
    {
        var result = Apply(Config((OverrideKey, "30d")));

        Assert.Equal(TimeSpan.FromDays(30), Find(result, "logs", "logs_ttl_created_at").ExpireAfter);
        Assert.Equal(TimeSpan.FromDays(90), Find(result, "usage_metrics", "usage_metrics_ttl").ExpireAfter);
    }

    [Fact]
    public void ValidOverride_PreservesOtherIndexFields()
    {
        var overridden = Find(Apply(Config((OverrideKey, "30d"))), "logs", "logs_ttl_created_at");

        Assert.Equal("logs_ttl_created_at", overridden.Name);
        Assert.Equal(new Dictionary<string, string> { ["created_at"] = "asc" }, overridden.Keys);
        Assert.True(overridden.Background);
        Assert.Null(overridden.Unique);
        Assert.Null(overridden.Sparse);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankOverride_LeavesYamlValue(string value)
    {
        var result = Apply(Config((OverrideKey, value)));

        Assert.Equal(TimeSpan.FromDays(15), Find(result, "logs", "logs_ttl_created_at").ExpireAfter);
    }

    [Fact]
    public void InvalidOverride_LeavesYamlValue()
    {
        var result = Apply(Config((OverrideKey, "thirty days")));

        Assert.Equal(TimeSpan.FromDays(15), Find(result, "logs", "logs_ttl_created_at").ExpireAfter);
    }

    [Fact]
    public void OverrideForIndexWithoutExpireAfter_IsIgnored()
    {
        var result = Apply(Config(("MongoIndexes:logs:workflow_run_id_1_autocreated:ExpireAfter", "1d")));

        Assert.Null(Find(result, "logs", "workflow_run_id_1_autocreated").ExpireAfter);
    }

    [Fact]
    public void OverrideForUnknownIndex_IsIgnored()
    {
        var result = Apply(Config(("MongoIndexes:logs:does_not_exist:ExpireAfter", "1d")));

        Assert.Equal(TimeSpan.FromDays(15), Find(result, "logs", "logs_ttl_created_at").ExpireAfter);
    }
}
