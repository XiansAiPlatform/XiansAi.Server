using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Services.HealthChecks;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// End-to-end test for built-in webhook checks: a real agent answers with <c>OnWebhook</c>,
/// and checks created through the API are run by the server's worker and graded with JSON checks.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalWebhookHealthCheckTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalWebhookHealthCheckTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task BuiltinWebhookCheck_RunsAgainstOnWebhookAndGradesJson()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"Webhook Checks {Guid.NewGuid():N}";
        const string activationName = "front-desk";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server, Temporal.TargetHost, Temporal.Namespace, tenantId, _adminUserId!);
        var agent = host.RegisterTemplate(agentName, "Answers builtin webhooks for health-check tests");
        agent.Workflows.DefineIntegrator().OnWebhook(context =>
        {
            context.Respond(JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["status"] = "ok",
                ["webhook"] = context.Webhook.Name,
                ["payload"] = context.Webhook.Payload ?? string.Empty
            }));
        });
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        BindTenantContext(tenantId, _adminUserId!);
        var passing = await CreateBuiltinCheckAsync(tenantId, activationId, "health ping ok", expectedStatus: "ok");
        var failing = await CreateBuiltinCheckAsync(tenantId, activationId, "health ping wrong", expectedStatus: "degraded");

        var passRun = await TriggerAndWaitForRunAsync(tenantId, passing);
        Assert.True(passRun.Status == HealthCheckStatus.Pass, $"Expected pass but got {passRun.Status}: {passRun.Detail}");
        Assert.All(passRun.AssertionResults, r => Assert.True(r.Passed));

        var failRun = await TriggerAndWaitForRunAsync(tenantId, failing);
        Assert.Equal(HealthCheckStatus.Fail, failRun.Status);
        Assert.Contains("expected $.status to equal 'degraded' but got ok", failRun.Detail);

        await host.DisposeAsync();
        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private async Task<AgentHealthCheck> CreateBuiltinCheckAsync(string tenantId, string activationId, string name, string expectedStatus)
    {
        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}/health-checks",
            new
            {
                name,
                type = "webhook",
                intervalMinutes = 60,
                timeoutSeconds = 30,
                webhook = new
                {
                    mode = "builtin",
                    workflowType = "Integrator Workflow",
                    webhookName = "health-ping",
                    body = "{\"probe\":true}",
                    expectedStatusCode = 200,
                    maxResponseMs = 30000,
                    assertions = new object[]
                    {
                        new { op = "jsonEquals", path = "$.status", value = expectedStatus },
                        new { op = "jsonEquals", path = "$.webhook", value = "health-ping" },
                        new { op = "jsonExists", path = "$.payload" }
                    }
                }
            });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadAsJsonAsync<AgentHealthCheck>(response))!;
    }

    private async Task<AgentHealthCheckRun> TriggerAndWaitForRunAsync(string tenantId, AgentHealthCheck check)
    {
        await Temporal.Environment.Client
            .GetScheduleHandle(HealthCheckScheduler.GetScheduleId(tenantId, check.Id))
            .TriggerAsync();

        for (var attempt = 0; attempt < 120; attempt++)
        {
            using var scope = _factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAgentHealthCheckRepository>();
            var run = (await repository.ListRunsAsync(tenantId, check.Id, null, null, null, 1, 1)).Items.FirstOrDefault();
            if (run != null)
            {
                return run;
            }
            await Task.Delay(500);
        }

        throw new TimeoutException($"Health check {check.Name} produced no run within 60s");
    }
}
