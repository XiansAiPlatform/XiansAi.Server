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
