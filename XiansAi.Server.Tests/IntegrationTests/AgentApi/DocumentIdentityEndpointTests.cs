using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

/// <summary>
/// The identity a <c>useKeyAsIdentifier</c> save resolves against, over HTTP with the real
/// repository. Reads go through <c>/query</c> with agent, activation and participant filters,
/// which is the path Xians.Lib's <c>GetByKeyAsync</c> takes.
/// </summary>
public class DocumentIdentityEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    private const string Type = "DailyTaskCounter";

    public DocumentIdentityEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    private static string NewKey() => $"2026-09-20-{Guid.NewGuid():N}";

    /// <summary>Query authorises against agents the caller can read, so seed a real agent record.</summary>
    private async Task<string> SeedAgentAsync(string prefix)
    {
        var agentName = $"{prefix} {Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var agents = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        await agents.CreateAsync(new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            OwnerAccess = ["test-user"],
            ReadAccess = ["test-user"],
            WriteAccess = ["test-user"],
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            SystemScoped = false
        });
        return agentName;
    }

    private async Task<HttpResponseMessage> SaveAsync(
        string agentId, string key, string owner, string? activationName, string? participantId = null, bool overwrite = true)
    {
        return await _client.PostAsJsonAsync("/api/agent/documents/save", new
        {
            document = new { agentId, activationName, participantId, type = Type, key, content = new { owner } },
            options = new { useKeyAsIdentifier = true, overwrite }
        });
    }

    private async Task<JsonElement> SaveOkAsync(string agentId, string key, string owner, string? activationName, string? participantId = null)
    {
        var response = await SaveAsync(agentId, key, owner, activationName, participantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>What the lib's GetByKeyAsync sends: type, key, agent, activation and participant from context.</summary>
    private async Task<string?> ReadOwnerAsync(string agentId, string key, string? activationName, string? participantId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/agent/documents/query", new
        {
            query = new { agentId, type = Type, key, activationName, participantId, limit = 1 }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documents = await response.Content.ReadFromJsonAsync<JsonElement>();
        return documents.GetArrayLength() == 0
            ? null
            : documents[0].GetProperty("content").GetProperty("owner").GetString();
    }

    private async Task<long> CountStoredAsync(string key)
    {
        return await _database.GetCollection<BsonDocument>("documents").CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Eq("tenant_id", TestTenantId) &
            Builders<BsonDocument>.Filter.Eq("type", Type) &
            Builders<BsonDocument>.Filter.Eq("key", key));
    }

    [Fact]
    public async Task TwoActivationsOfOneAgent_SavingSameTypeAndKey_KeepSeparateDocuments()
    {
        var agent = await SeedAgentAsync("Counter");
        var key = NewKey();

        var frontDesk = await SaveOkAsync(agent, key, "front-desk", "front-desk");
        var backOffice = await SaveOkAsync(agent, key, "back-office", "back-office");

        Assert.NotEqual(frontDesk.GetProperty("id").GetString(), backOffice.GetProperty("id").GetString());
        Assert.Equal(2, await CountStoredAsync(key));
        Assert.Equal("front-desk", await ReadOwnerAsync(agent, key, "front-desk"));
        Assert.Equal("back-office", await ReadOwnerAsync(agent, key, "back-office"));
    }

    [Fact]
    public async Task TwoAgents_SavingSameTypeAndKey_NeitherReplacesTheOther()
    {
        var agentA = await SeedAgentAsync("Agent A");
        var agentB = await SeedAgentAsync("Agent B");
        var key = NewKey();

        var a = await SaveOkAsync(agentA, key, "A", "front-desk");
        var b = await SaveOkAsync(agentB, key, "B", "front-desk");
        var bAgain = await SaveOkAsync(agentB, key, "B2", "front-desk");

        Assert.NotEqual(a.GetProperty("id").GetString(), b.GetProperty("id").GetString());
        Assert.Equal(b.GetProperty("id").GetString(), bAgain.GetProperty("id").GetString());
        Assert.Equal(2, await CountStoredAsync(key));
        Assert.Equal("A", await ReadOwnerAsync(agentA, key, "front-desk"));
        Assert.Equal("B2", await ReadOwnerAsync(agentB, key, "front-desk"));
    }

    [Fact]
    public async Task TwoParticipants_KeepSeparateDocuments_AndNoParticipantIsItsOwnSlot()
    {
        var agent = await SeedAgentAsync("Participants");
        var key = NewKey();

        var scheduled = await SaveOkAsync(agent, key, "scheduled-run", "front-desk", participantId: null);
        await SaveOkAsync(agent, key, "alice", "front-desk", "alice@example.com");
        await SaveOkAsync(agent, key, "bob", "front-desk", "bob@example.com");
        var scheduledAgain = await SaveOkAsync(agent, key, "scheduled-run-v2", "front-desk", participantId: "");

        Assert.Equal(3, await CountStoredAsync(key));
        Assert.Equal(scheduled.GetProperty("id").GetString(), scheduledAgain.GetProperty("id").GetString());
        Assert.Equal("alice", await ReadOwnerAsync(agent, key, "front-desk", "alice@example.com"));
        Assert.Equal("bob", await ReadOwnerAsync(agent, key, "front-desk", "bob@example.com"));
        Assert.Null(await ReadOwnerAsync(agent, key, "front-desk", "carol@example.com"));
    }

    [Fact]
    public async Task Save_OverwriteFalse_RejectsOnlyTheCallersOwnDocument()
    {
        var agent = await SeedAgentAsync("Overwrite");
        var key = NewKey();
        await SaveOkAsync(agent, key, "first", "front-desk");

        var sameSlot = await SaveAsync(agent, key, "second", "front-desk", overwrite: false);
        var otherSlot = await SaveAsync(agent, key, "back-office", "back-office", overwrite: false);

        Assert.Equal(HttpStatusCode.Conflict, sameSlot.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherSlot.StatusCode);
        Assert.Equal("first", await ReadOwnerAsync(agent, key, "front-desk"));
        Assert.Equal(2, await CountStoredAsync(key));
    }
}
