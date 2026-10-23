using System.Net;
using System.Net.Http.Json;
using Shared.Data.Models;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

public class KnowledgeCreateEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public KnowledgeCreateEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    private static object Body(string name, string content, string agent, bool systemScoped = false) => new
    {
        name,
        content,
        type = "text",
        agent,
        systemScoped
    };

    [Fact]
    public async Task Post_NewKnowledge_Returns201WithKnowledgeBody()
    {
        var name = $"kn-{Guid.NewGuid()}";

        var response = await _client.PostAsJsonAsync("/api/agent/knowledge", Body(name, "content", "test-agent"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var result = await response.Content.ReadFromJsonAsync<Knowledge>();
        Assert.NotNull(result);
        Assert.Equal(name, result.Name);
        Assert.Equal(result.CreatedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task Post_ExistingKnowledge_Returns200AndBumpsUpdatedAt()
    {
        var name = $"kn-{Guid.NewGuid()}";
        var first = await _client.PostAsJsonAsync("/api/agent/knowledge", Body(name, "content", "test-agent"));
        var created = await first.Content.ReadFromJsonAsync<Knowledge>();
        await Task.Delay(100);

        var second = await _client.PostAsJsonAsync("/api/agent/knowledge", Body(name, "content", "test-agent"));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var again = await second.Content.ReadFromJsonAsync<Knowledge>();
        Assert.NotNull(created);
        Assert.NotNull(again);
        Assert.Equal(created.Id, again.Id);
        // Mongo stores milliseconds; the first response carries the in-memory value
        Assert.True((created.CreatedAt - again.CreatedAt).Duration() < TimeSpan.FromMilliseconds(5));
        Assert.True(again.UpdatedAt > created.UpdatedAt);
    }
}
