using System.Net;
using System.Text;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AppsApi;

/// <summary>
/// Smoke test for the AppsApi generic webhook router. The router is public (authenticated via
/// per-integration secret), so an unknown integration id must return Not Found without leaking
/// whether the integration exists.
/// </summary>
public class AppWebhookEndpointsTests : IntegrationTestBase
{
    public AppWebhookEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GenericWebhook_WithUnknownIntegration_ReturnsNotFound()
    {
        var content = new StringContent("{\"event\": \"ping\"}", Encoding.UTF8, "application/json");
        var url = $"/api/apps/webhook/events/{Guid.NewGuid()}/some-secret";

        var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
