using Features.AgentApi.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Repositories;

/// <summary>
/// DB-level check that <see cref="DocumentRepository.GetByIdentityAsync"/> distinguishes the
/// agent, activation and participant slots against a real MongoDB (ephemeral fixture).
/// </summary>
public class DocumentRepositoryIdentityTests : IClassFixture<MongoDbFixture>
{
    private readonly IMongoCollection<BsonDocument> _raw;
    private readonly DocumentRepository _repository;

    public DocumentRepositoryIdentityTests(MongoDbFixture fixture)
    {
        _raw = fixture.Database.GetCollection<BsonDocument>("documents");
        _repository = new DocumentRepository(new FixtureDatabaseService(fixture.Database), NullLogger<DocumentRepository>.Instance);
    }

    private sealed class FixtureDatabaseService(IMongoDatabase database) : IDatabaseService
    {
        public Task<IMongoDatabase> GetDatabaseAsync() => Task.FromResult(database);
    }

    private static Document NewDocument(string tenant, string agent, string? activation, string? participant = null) => new()
    {
        TenantId = tenant,
        AgentId = agent,
        Type = "Config",
        Key = "country",
        ActivationName = activation,
        ParticipantId = participant,
        Content = new BsonDocument("v", 1)
    };

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private Task<Document?> FindAsync(string tenant, string agent, string? activation, string? participant = null) =>
        _repository.GetByIdentityAsync(new DocumentIdentity(tenant, agent, "Config", "country", activation, participant));

    [Fact]
    public async Task GetByIdentity_DistinguishesAgentsActivationsAndParticipants()
    {
        var tenant = Unique("tenant");
        var none = await _repository.CreateAsync(NewDocument(tenant, "Agent A", null));
        var frontDesk = await _repository.CreateAsync(NewDocument(tenant, "Agent A", "front-desk"));
        var backOffice = await _repository.CreateAsync(NewDocument(tenant, "Agent A", "back-office"));
        var alice = await _repository.CreateAsync(NewDocument(tenant, "Agent A", "front-desk", "alice"));
        var otherAgent = await _repository.CreateAsync(NewDocument(tenant, "Agent B", "front-desk"));

        Assert.Equal(none.Id, (await FindAsync(tenant, "Agent A", null))!.Id);
        Assert.Equal(frontDesk.Id, (await FindAsync(tenant, "Agent A", "front-desk"))!.Id);
        Assert.Equal(backOffice.Id, (await FindAsync(tenant, "Agent A", "back-office"))!.Id);
        Assert.Equal(alice.Id, (await FindAsync(tenant, "Agent A", "front-desk", "alice"))!.Id);
        Assert.Equal(otherAgent.Id, (await FindAsync(tenant, "Agent B", "front-desk"))!.Id);

        Assert.Null(await FindAsync(tenant, "Agent B", null));
        Assert.Null(await FindAsync(tenant, "Agent A", "front-desk", "bob"));
        Assert.Null(await FindAsync(Unique("other-tenant"), "Agent A", null));
    }

    [Fact]
    public async Task GetByIdentity_NullSlots_MatchDocumentsWithoutTheFields()
    {
        // Documents written before the stamps existed have no activation_name / participant_id at all.
        var tenant = Unique("tenant");
        var legacy = new BsonDocument
        {
            { "tenant_id", tenant }, { "agent_id", "Agent A" }, { "type", "Config" }, { "key", "country" },
            { "content", new BsonDocument("v", 1) }, { "created_at", DateTime.UtcNow }
        };
        await _raw.InsertOneAsync(legacy);

        var found = await FindAsync(tenant, "Agent A", null);

        Assert.Equal(legacy["_id"].AsObjectId.ToString(), found!.Id);
    }
}
