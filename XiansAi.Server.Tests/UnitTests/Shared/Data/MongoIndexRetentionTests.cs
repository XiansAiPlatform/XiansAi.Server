using Shared.Data;

namespace Tests.UnitTests.Shared.Data;

public class MongoIndexRetentionTests
{
    private static async Task<MongoIndexDefinition> FindAsync(string collection, string name)
    {
        var definitions = await MongoIndexSynchronizer.ReadEmbeddedIndexDefinitionsAsync();

        Assert.True(definitions.ContainsKey(collection), $"Collection '{collection}' is missing from mongodb-indexes.yaml");
        return definitions[collection].Single(d => d.Name == name);
    }

    [Fact]
    public async Task AuditLogs_ExpireTwelveMonthsAfterCreation()
    {
        var index = await FindAsync("audit_logs", "audit_logs_ttl");

        Assert.Equal(new Dictionary<string, string> { ["created_at"] = "asc" }, index.Keys);
        Assert.Equal(TimeSpan.FromDays(365), index.ExpireAfter);
    }

    [Fact]
    public async Task ActivityHistory_ExpiresNinetyDaysAfterStart()
    {
        var index = await FindAsync("activity_history", "activity_history_ttl");

        Assert.Equal(new Dictionary<string, string> { ["started_time"] = "asc" }, index.Keys);
        Assert.Equal(TimeSpan.FromDays(90), index.ExpireAfter);
    }
}
