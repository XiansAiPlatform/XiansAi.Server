using System.Net;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

/// <summary>
/// Smoke tests for the AgentApi definitions group. The group has no plain list endpoint, so the
/// hash-check read is used to verify routing, certificate auth, and the not-found path.
/// </summary>
public class DefinitionsEndpointsTests : IntegrationTestBase
{
    public DefinitionsEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task CheckHash_ForUnknownDefinition_ReturnsNotFound()
    {
        var response = await _client.GetAsync(
            "/api/agent/definitions/check?workflowType=nonexistent-workflow&systemScoped=false&hash=deadbeef");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CheckHash_WithoutHashParam_ReturnsBadRequest()
    {
        // The required 'hash' query parameter is missing, so model binding rejects the request.
        var response = await _client.GetAsync(
            "/api/agent/definitions/check?workflowType=some-workflow&systemScoped=false");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
