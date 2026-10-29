using Shared.Data;
using Shared.Utils.Serialization;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Tests.UnitTests.Shared.Privacy;

public class PrivacyRequestIndexTests
{
    private static List<MongoIndexDefinition> PrivacyRequestIndexes() => Indexes("privacy_requests");

    private static List<MongoIndexDefinition> Indexes(string collection)
    {
        var assembly = typeof(MongoIndexSynchronizer).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.mongodb-indexes.yaml")!;
        using var reader = new StreamReader(stream);

        var definitions = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new TimeSpanTypeConverter())
            .Build()
            .Deserialize<Dictionary<string, List<MongoIndexDefinition>>>(reader.ReadToEnd());

        return definitions[collection];
    }

    [Fact]
    public void Records_ExpireThreeYearsAfterTheyClose()
    {
        var ttl = Assert.Single(PrivacyRequestIndexes(), index => index.ExpireAfter.HasValue);

        Assert.Equal("privacy_request_ttl", ttl.Name);
        Assert.Equal(new Dictionary<string, string> { ["closed_at"] = "asc" }, ttl.Keys);
        Assert.Equal(TimeSpan.FromDays(1095), ttl.ExpireAfter);
    }

    [Fact]
    public void TenantListing_SortsByCreationWithOrWithoutAStatusFilter()
    {
        var index = Assert.Single(PrivacyRequestIndexes(), index => index.Name == "privacy_request_tenant");

        Assert.Equal(["scope.tenant_ids", "created_at", "status"], index.Keys.Keys);
    }

    [Theory]
    [InlineData("privacy_request_claim", "status")]
    [InlineData("privacy_request_tenant", "scope.tenant_ids")]
    [InlineData("privacy_request_requester", "requested_by.user_hash")]
    public void QueryIndexes_LeadWithTheFilteredField(string name, string firstKey)
    {
        var index = Assert.Single(PrivacyRequestIndexes(), index => index.Name == name);

        Assert.Equal(firstKey, index.Keys.Keys.First());
    }

    [Fact]
    public void Threads_CanBeFoundByParticipantAcrossTenants()
    {
        var index = Assert.Single(Indexes("conversation_thread"), index => index.Name == "thread_participant_lookup");

        Assert.Equal(["participant_id", "tenant_id", "workflow_id"], index.Keys.Keys);
    }
}
