using MongoDB.Bson;
using Shared.Data;

namespace Tests.UnitTests.Shared.Data;

public class MongoIndexDefinitionTests
{
    private static MongoIndexDefinition DomainIndex(bool? unique = null) => new()
    {
        Name = "domain_1",
        Keys = new Dictionary<string, string> { ["domain"] = "asc" },
        Unique = unique
    };

    private static BsonDocument ExistingIndex(BsonDocument keys, bool? unique = null, bool? sparse = null)
    {
        var index = new BsonDocument
        {
            { "v", 2 },
            { "name", "domain_1" },
            { "key", keys }
        };
        if (unique.HasValue) index["unique"] = unique.Value;
        if (sparse.HasValue) index["sparse"] = sparse.Value;
        return index;
    }

    [Fact]
    public void MatchesExistingIndex_IgnoresServerMetadata_WhenKeysAndOptionsAgree()
    {
        var existing = ExistingIndex(new BsonDocument { { "domain", 1 } });
        existing["ns"] = "xiansai.tenants";

        Assert.True(DomainIndex().MatchesExistingIndex(existing));
    }

    [Fact]
    public void MatchesExistingIndex_DetectsUniqueConstraintThatDefinitionNoLongerAsksFor()
    {
        var existing = ExistingIndex(new BsonDocument { { "domain", 1 } }, unique: true);

        Assert.False(DomainIndex().MatchesExistingIndex(existing));
    }

    [Fact]
    public void MatchesExistingIndex_DetectsMissingUniqueConstraint()
    {
        var existing = ExistingIndex(new BsonDocument { { "domain", 1 } });

        Assert.False(DomainIndex(unique: true).MatchesExistingIndex(existing));
    }

    [Fact]
    public void MatchesExistingIndex_DetectsSparseDifference()
    {
        var existing = ExistingIndex(new BsonDocument { { "domain", 1 } }, sparse: true);

        Assert.False(DomainIndex().MatchesExistingIndex(existing));
    }

    [Fact]
    public void MatchesExistingIndex_DetectsChangedKeyDirection()
    {
        var existing = ExistingIndex(new BsonDocument { { "domain", -1 } });

        Assert.False(DomainIndex().MatchesExistingIndex(existing));
    }

    [Fact]
    public void MatchesExistingIndex_ComparesCompoundKeyOrder()
    {
        var definition = new MongoIndexDefinition
        {
            Name = "tenant_1_created_at_-1",
            Keys = new Dictionary<string, string> { ["tenant"] = "asc", ["created_at"] = "desc" }
        };

        var sameOrder = new BsonDocument
        {
            { "name", "tenant_1_created_at_-1" },
            { "key", new BsonDocument { { "tenant", 1 }, { "created_at", -1 } } }
        };
        var reversedOrder = new BsonDocument
        {
            { "name", "tenant_1_created_at_-1" },
            { "key", new BsonDocument { { "created_at", -1 }, { "tenant", 1 } } }
        };

        Assert.True(definition.MatchesExistingIndex(sameOrder));
        Assert.False(definition.MatchesExistingIndex(reversedOrder));
    }

    [Fact]
    public void MatchesExistingIndex_ComparesTimeToLive()
    {
        var definition = new MongoIndexDefinition
        {
            Name = "logs_ttl_created_at",
            Keys = new Dictionary<string, string> { ["created_at"] = "asc" },
            ExpireAfter = TimeSpan.FromDays(15)
        };

        var matchingTtl = new BsonDocument
        {
            { "name", "logs_ttl_created_at" },
            { "key", new BsonDocument { { "created_at", 1 } } },
            { "expireAfterSeconds", TimeSpan.FromDays(15).TotalSeconds }
        };
        var differentTtl = new BsonDocument
        {
            { "name", "logs_ttl_created_at" },
            { "key", new BsonDocument { { "created_at", 1 } } },
            { "expireAfterSeconds", TimeSpan.FromDays(30).TotalSeconds }
        };
        var noTtl = new BsonDocument
        {
            { "name", "logs_ttl_created_at" },
            { "key", new BsonDocument { { "created_at", 1 } } }
        };

        Assert.True(definition.MatchesExistingIndex(matchingTtl));
        Assert.False(definition.MatchesExistingIndex(differentTtl));
        Assert.False(definition.MatchesExistingIndex(noTtl));
    }

    [Fact]
    public void MatchesExistingIndex_DetectsUnexpectedTimeToLive()
    {
        var existing = ExistingIndex(new BsonDocument { { "domain", 1 } });
        existing["expireAfterSeconds"] = 3600;

        Assert.False(DomainIndex().MatchesExistingIndex(existing));
    }
}
