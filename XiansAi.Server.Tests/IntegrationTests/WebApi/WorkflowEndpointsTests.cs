using System.Net;
using Tests.TestUtils;

namespace Tests.IntegrationTests.WebApi;

/// <summary>
/// Smoke tests for the WebApi workflows group. These exercise routing, tenant auth, and the
/// request-validation paths that do not require a live Temporal server (the finder service
/// validates its inputs before contacting Temporal).
/// </summary>
public class WorkflowEndpointsTests : WebApiIntegrationTestBase
{
    public WorkflowEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetWorkflow_WithEmptyWorkflowId_ReturnsBadRequest()
    {
        // The finder service rejects an empty workflow id before reaching Temporal.
        var response = await GetAsync("/api/client/workflows/?workflowId=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkflowTypes_WithoutAgentParam_ReturnsBadRequest()
    {
        // The required 'agent' query parameter is missing, so model binding rejects the request.
        var response = await GetAsync("/api/client/workflows/types");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
