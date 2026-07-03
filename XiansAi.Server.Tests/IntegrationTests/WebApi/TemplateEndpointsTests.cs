using System.Net;
using System.Net.Http.Json;
using Tests.TestUtils;

namespace Tests.IntegrationTests.WebApi;

/// <summary>
/// Smoke tests for the WebApi templates group. The WebApi settings group only exposes
/// certificate generation (which needs a real signing PFX and cannot run in-process), so the
/// templates group is used to cover this area with a deterministic, Mongo-backed read.
/// </summary>
public class TemplateEndpointsTests : WebApiIntegrationTestBase
{
    public TemplateEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetSystemScopedAgents_ReturnsOk()
    {
        var response = await GetAsync("/api/client/templates/agents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agents = await response.Content.ReadFromJsonAsync<List<object>>();
        Assert.NotNull(agents);
    }

    [Fact]
    public async Task GetSystemScopedAgents_WithBasicDataOnly_ReturnsOk()
    {
        var response = await GetAsync("/api/client/templates/agents?basicDataOnly=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
