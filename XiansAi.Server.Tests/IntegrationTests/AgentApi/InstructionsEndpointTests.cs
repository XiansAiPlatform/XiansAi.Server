using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using Shared.Data.Models;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

public class InstructionsEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public InstructionsEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetLatestInstruction_ReturnsLatestInstruction()
    {
        // Arrange - Create a unique name for this test run
        string testInstructionName = $"test-instruction-{Guid.NewGuid()}";

        // Insert multiple instructions with the same name but different timestamps
        var knowledge = new List<Knowledge>
        {
            new Knowledge
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = testInstructionName,
                Content = "First instruction content",
                Type = "text",
                Version = "v1",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                UpdatedAt = DateTime.UtcNow.AddHours(-2),
                CreatedBy = "test-agent",
                Agent = "test-agent",
                TenantId = TestTenantId
            },
            new Knowledge
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = testInstructionName,
                Content = "Second instruction content",
                Type = "text",
                Version = "v2",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                UpdatedAt = DateTime.UtcNow.AddHours(-1),
                CreatedBy = "test-agent",
                Agent = "test-agent",
                TenantId = TestTenantId
            },
            new Knowledge
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = testInstructionName,
                Content = "Latest instruction content",
                Type = "text",
                Version = "v3",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "test-agent",
                Agent = "test-agent",
                TenantId = TestTenantId
            }
        };

        // Insert the instructions into the database
        var collection = _database.GetCollection<Knowledge>("knowledge");
        await collection.InsertManyAsync(knowledge);

        // Act - Call the endpoint to get the latest instruction
        var response = await _client.GetAsync($"/api/agent/knowledge/latest?name={testInstructionName}&agent=test-agent");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<Knowledge>();
        Assert.NotNull(result);
        Assert.Equal(testInstructionName, result.Name);
        Assert.Equal("Latest instruction content", result.Content);
        Assert.Equal("v3", result.Version);
    }

    private static Knowledge Row(string name, string content, string version, DateTime createdAt, DateTime? updatedAt) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Name = name,
        Content = content,
        Type = "text",
        Version = version,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt,
        CreatedBy = "test-agent",
        Agent = "test-agent",
        TenantId = TestTenantId
    };

    private async Task<Knowledge?> GetLatestAsync(string name)
    {
        var response = await _client.GetAsync($"/api/agent/knowledge/latest?name={name}&agent=test-agent");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<Knowledge>();
    }

    [Fact]
    public async Task GetLatest_MixedRows_PrefersRowsWithUpdatedAtOverNewerLegacyRow()
    {
        var name = $"mixed-{Guid.NewGuid()}";
        var now = DateTime.UtcNow;
        await _database.GetCollection<Knowledge>("knowledge").InsertManyAsync(new[]
        {
            // legacy row (no updated_at) with the newest created_at
            Row(name, "legacy newest", "v-legacy", now, null),
            // rows with updated_at, created earlier
            Row(name, "has updated_at old", "v-old", now.AddHours(-5), now.AddHours(-3)),
            Row(name, "has updated_at new", "v-new", now.AddHours(-4), now.AddHours(-2)),
        });

        var latest = await GetLatestAsync(name);

        Assert.Equal("has updated_at new", latest!.Content);
    }

    [Fact]
    public async Task GetLatest_OnlyLegacyRows_FallsBackToCreatedAt()
    {
        var name = $"legacy-{Guid.NewGuid()}";
        var now = DateTime.UtcNow;
        await _database.GetCollection<Knowledge>("knowledge").InsertManyAsync(new[]
        {
            Row(name, "legacy older", "v1", now.AddHours(-3), null),
            Row(name, "legacy newest", "v2", now.AddHours(-1), null),
            Row(name, "legacy middle", "v3", now.AddHours(-2), null),
        });

        var latest = await GetLatestAsync(name);

        Assert.Equal("legacy newest", latest!.Content);
        Assert.Null(latest.UpdatedAt);
    }

    [Fact]
    public async Task GetLatest_AllRowsHaveUpdatedAt_UsesUpdatedAtNotCreatedAt()
    {
        var name = $"updated-{Guid.NewGuid()}";
        var now = DateTime.UtcNow;
        await _database.GetCollection<Knowledge>("knowledge").InsertManyAsync(new[]
        {
            // newest created_at but oldest updated_at
            Row(name, "created newest", "v1", now.AddHours(-1), now.AddHours(-5)),
            // older created_at but most recently updated
            Row(name, "updated newest", "v2", now.AddHours(-4), now.AddHours(-2)),
        });

        var latest = await GetLatestAsync(name);

        Assert.Equal("updated newest", latest!.Content);
    }

    [Fact]
    public async Task GetLatestInstruction_WithNonExistentName_ReturnsNotFound()
    {
        // Arrange - Generate a name that shouldn't exist in the database
        string nonExistentName = $"non-existent-instruction-{Guid.NewGuid()}";

        // Act
        var response = await _client.GetAsync($"/api/agent/knowledge/latest?name={nonExistentName}&agent=test-agent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
