using System.Net;
using System.Text.Json;
using Features.AdminApi.Auth;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// AdminApi health-check endpoints against Mongo, with Temporal scheduling replaced by
/// <see cref="FakeHealthCheckScheduler"/>.
/// </summary>
public class AdminHealthCheckEndpointsTests : AdminApiIntegrationTestBase
{
    private const string MatrixRoute = "/api/v1/admin/admin-console/capability-matrix";

    public AdminHealthCheckEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    private FakeHealthCheckScheduler Scheduler => _factory.Services.GetRequiredService<FakeHealthCheckScheduler>();

    private static string ChecksRoute(string tenantId, string activationId)
        => $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}/health-checks";

    private async Task<(string TenantId, AgentActivation Activation)> SeedAsync(bool activationActive = true, string role = SystemRoles.TenantAdmin)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId, role);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid():N}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: activationActive);
        return (tenantId, activation);
    }

    private static object ChatCheck(string name = "greets user", int intervalMinutes = 15) => new
    {
        name,
        type = "chat",
        intervalMinutes,
        timeoutSeconds = 30,
        chat = new
        {
            message = "Hello",
            assertions = new[] { new { op = "contains", value = "hello" } }
        }
    };

    private async Task<AgentHealthCheck> CreateCheckAsync(string tenantId, string activationId, object? body = null)
    {
        var response = await PostAsJsonAsync(ChecksRoute(tenantId, activationId), body ?? ChatCheck());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsJsonAsync<AgentHealthCheck>(response))!;
    }

    [Fact]
    public async Task CreateChatCheck_AsTenantAdmin_PersistsAndSchedulesIt()
    {
        var (tenantId, activation) = await SeedAsync();

        var check = await CreateCheckAsync(tenantId, activation.Id);

        Assert.False(string.IsNullOrEmpty(check.Id));
        Assert.Equal(HealthCheckType.Chat, check.Type);
        Assert.Equal(activation.AgentName, check.AgentName);
        Assert.Equal(activation.Name, check.ActivationName);
        Assert.Equal(HealthCheckScheduleState.Active, check.ScheduleState);
        Assert.Equal(HealthCheckAssertionOp.Contains, Assert.Single(check.Chat!.Assertions).Op);

        var scheduled = Scheduler.Get(tenantId, check.Id);
        Assert.NotNull(scheduled);
        Assert.False(scheduled!.Paused);
        Assert.Equal(15, scheduled.IntervalMinutes);

        var get = await GetAsync($"{ChecksRoute(tenantId, activation.Id)}/{check.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var list = await ReadAsJsonAsync<List<AgentHealthCheck>>(await GetAsync(ChecksRoute(tenantId, activation.Id)));
        Assert.Equal(check.Id, Assert.Single(list!).Id);
    }

    [Fact]
    public async Task CreateCheck_OnInactiveActivation_IsScheduledPaused()
    {
        var (tenantId, activation) = await SeedAsync(activationActive: false);

        var check = await CreateCheckAsync(tenantId, activation.Id);

        Assert.Equal(HealthCheckScheduleState.Paused, check.ScheduleState);
        Assert.True(Scheduler.Get(tenantId, check.Id)!.Paused);
    }

    [Fact]
    public async Task CreateCheck_WithIntervalBelowFiveMinutes_ReturnsBadRequest()
    {
        var (tenantId, activation) = await SeedAsync();

        var response = await PostAsJsonAsync(ChecksRoute(tenantId, activation.Id), ChatCheck(intervalMinutes: 4));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("intervalMinutes", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateCheck_WithPayloadNotMatchingType_ReturnsBadRequest()
    {
        var (tenantId, activation) = await SeedAsync();

        var response = await PostAsJsonAsync(ChecksRoute(tenantId, activation.Id), new
        {
            name = "mismatch",
            type = "webhook",
            intervalMinutes = 10,
            chat = new { message = "hi" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCheck_WithDuplicateName_ReturnsConflict()
    {
        var (tenantId, activation) = await SeedAsync();
        await CreateCheckAsync(tenantId, activation.Id);

        var response = await PostAsJsonAsync(ChecksRoute(tenantId, activation.Id), ChatCheck());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateBuiltinWebhookCheck_WithStructuredAssertions_RoundTrips()
    {
        var (tenantId, activation) = await SeedAsync();

        var check = await CreateCheckAsync(tenantId, activation.Id, new
        {
            name = "integrator webhook",
            type = "webhook",
            intervalMinutes = 15,
            timeoutSeconds = 20,
            webhook = new
            {
                mode = "builtin",
                workflowType = "Integrator Workflow",
                webhookName = "ticket-created",
                body = "{\"id\":1}",
                expectedStatusCode = 200,
                maxResponseMs = 5000,
                assertions = new object[]
                {
                    new { op = "jsonEquals", path = "$.status", value = "ok" },
                    new { op = "headerExists", path = "X-Request-Id" }
                }
            }
        });

        Assert.Equal(WebhookHealthCheckMode.Builtin, check.Webhook!.Mode);
        Assert.Equal("ticket-created", check.Webhook.WebhookName);
        Assert.Equal(5000, check.Webhook.MaxResponseMs);
        Assert.Equal(
            [(HealthCheckAssertionOp.JsonEquals, "$.status"), (HealthCheckAssertionOp.HeaderExists, "X-Request-Id")],
            check.Webhook.Assertions.Select(a => (a.Op, a.Path!)).ToArray());

        var reloaded = (await ReadAsJsonAsync<AgentHealthCheck>(await GetAsync($"{ChecksRoute(tenantId, activation.Id)}/{check.Id}")))!;
        Assert.Equal(WebhookHealthCheckMode.Builtin, reloaded.Webhook!.Mode);
        Assert.Equal("$.status", reloaded.Webhook.Assertions[0].Path);
    }

    [Fact]
    public async Task CreateChatCheck_WithJsonAssertion_ReturnsBadRequest()
    {
        var (tenantId, activation) = await SeedAsync();

        var response = await PostAsJsonAsync(ChecksRoute(tenantId, activation.Id), new
        {
            name = "chat json",
            type = "chat",
            intervalMinutes = 15,
            chat = new { message = "Hi", assertions = new[] { new { op = "jsonEquals", path = "$.a", value = "b" } } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("only supported on webhook checks", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateScheduleCheck_ForUnknownSchedule_ReturnsBadRequest()
    {
        var (tenantId, activation) = await SeedAsync();

        var response = await PostAsJsonAsync(ChecksRoute(tenantId, activation.Id), new
        {
            name = "nightly sync",
            type = "schedule",
            intervalMinutes = 60,
            schedule = new { scheduleId = "no-such-schedule" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no-such-schedule", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UpdateCheck_Disabling_PausesScheduleAndChangingTypeIsRejected()
    {
        var (tenantId, activation) = await SeedAsync();
        var check = await CreateCheckAsync(tenantId, activation.Id);
        var route = $"{ChecksRoute(tenantId, activation.Id)}/{check.Id}";

        var update = await PutAsJsonAsync(route, new
        {
            name = "greets user - renamed",
            type = "chat",
            enabled = false,
            intervalMinutes = 30,
            chat = new { message = "Hi there", assertions = new[] { new { op = "notContains", value = "error" } } }
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await ReadAsJsonAsync<AgentHealthCheck>(update))!;
        Assert.False(updated.Enabled);
        Assert.Equal("greets user - renamed", updated.Name);
        Assert.Equal(HealthCheckScheduleState.Paused, updated.ScheduleState);
        Assert.Equal(HealthCheckAssertionOp.NotContains, Assert.Single(updated.Chat!.Assertions).Op);

        var scheduled = Scheduler.Get(tenantId, check.Id)!;
        Assert.True(scheduled.Paused);
        Assert.Equal(30, scheduled.IntervalMinutes);

        var typeChange = await PutAsJsonAsync(route, new
        {
            name = "now a webhook",
            type = "webhook",
            intervalMinutes = 30,
            webhook = new { workflowType = "Inbound", methodName = "ping" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, typeChange.StatusCode);
    }

    [Fact]
    public async Task DeleteCheck_RemovesCheckScheduleAndRuns()
    {
        var (tenantId, activation) = await SeedAsync();
        var check = await CreateCheckAsync(tenantId, activation.Id);
        await SeedRunsAsync(tenantId, check, HealthCheckStatus.Pass);
        var route = $"{ChecksRoute(tenantId, activation.Id)}/{check.Id}";

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(route)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(route)).StatusCode);
        Assert.Null(Scheduler.Get(tenantId, check.Id));

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentHealthCheckRepository>();
        var runs = await repository.ListRunsAsync(tenantId, check.Id, null, null, null, 1, 10);
        Assert.Equal(0, runs.TotalCount);
    }

    [Fact]
    public async Task ManageTestsCapability_WhenNotGranted_Returns403()
    {
        var (tenantId, activation) = await SeedAsync();
        var check = await CreateCheckAsync(tenantId, activation.Id);

        await ConfigureAdminApiClientAsync(tenantId); // SysAdmin, to edit the matrix
        var narrow = await PutAsJsonAsync($"{MatrixRoute}/{CapabilityActions.TenantHealthChecksManage}",
            new { allowedRoles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, narrow.StatusCode);

        try
        {
            await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
            var route = ChecksRoute(tenantId, activation.Id);

            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsJsonAsync(route, ChatCheck("another"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutAsJsonAsync($"{route}/{check.Id}", ChatCheck())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"{route}/{check.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"{route}/{check.Id}/runs")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"/api/v1/admin/tenants/{tenantId}/health-checks")).StatusCode);
        }
        finally
        {
            await ConfigureAdminApiClientAsync(tenantId);
            await DeleteAsync($"{MatrixRoute}/{CapabilityActions.TenantHealthChecksManage}");
        }
    }

    [Fact]
    public async Task ActivationFromAnotherTenant_ReturnsNotFound()
    {
        var (tenantA, activation) = await SeedAsync();
        var check = await CreateCheckAsync(tenantA, activation.Id);

        var tenantB = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantB);
        await ConfigureAdminApiClientAsync(tenantB, SystemRoles.TenantAdmin);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(ChecksRoute(tenantB, activation.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsJsonAsync(ChecksRoute(tenantB, activation.Id), ChatCheck("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"{ChecksRoute(tenantB, activation.Id)}/{check.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeactivatingActivation_PausesItsChecks()
    {
        var (tenantId, activation) = await SeedAsync();
        var check = await CreateCheckAsync(tenantId, activation.Id);
        Assert.False(Scheduler.Get(tenantId, check.Id)!.Paused);

        var deactivate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activation.Id}/deactivate", new { });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        Assert.True(Scheduler.Get(tenantId, check.Id)!.Paused);
        var after = (await ReadAsJsonAsync<AgentHealthCheck>(await GetAsync($"{ChecksRoute(tenantId, activation.Id)}/{check.Id}")))!;
        Assert.Equal(HealthCheckScheduleState.Paused, after.ScheduleState);
        Assert.True(after.Enabled); // the definition itself is untouched; reactivation resumes it
    }

    [Fact]
    public async Task DeletingActivation_DeletesItsChecks()
    {
        var (tenantId, activation) = await SeedAsync(activationActive: false);
        var check = await CreateCheckAsync(tenantId, activation.Id);

        var delete = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations/{activation.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        Assert.Null(Scheduler.Get(tenantId, check.Id));
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentHealthCheckRepository>();
        Assert.Null(await repository.GetByIdAsync(tenantId, check.Id));
    }

    [Fact]
    public async Task RunsAndSummary_ReflectRecordedResults()
    {
        var (tenantId, activation) = await SeedAsync();
        var passing = await CreateCheckAsync(tenantId, activation.Id, ChatCheck("passing"));
        var failing = await CreateCheckAsync(tenantId, activation.Id, ChatCheck("failing"));
        await SeedRunsAsync(tenantId, passing, HealthCheckStatus.Fail, HealthCheckStatus.Pass);
        await SeedRunsAsync(tenantId, failing, HealthCheckStatus.Pass, HealthCheckStatus.Fail);

        var runsResponse = await GetAsync($"{ChecksRoute(tenantId, activation.Id)}/{passing.Id}/runs?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, runsResponse.StatusCode);
        using (var runs = JsonDocument.Parse(await runsResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(2, runs.RootElement.GetProperty("totalCount").GetInt64());
            var newest = Assert.Single(runs.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("pass", newest.GetProperty("status").GetString());
        }

        var failedOnly = await GetAsync($"{ChecksRoute(tenantId, activation.Id)}/{passing.Id}/runs?status=fail");
        using (var runs = JsonDocument.Parse(await failedOnly.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, runs.RootElement.GetProperty("totalCount").GetInt64());
        }

        var summary = (await ReadAsJsonAsync<ActivationHealthSummary>(
            await GetAsync($"{ChecksRoute(tenantId, activation.Id)}/summary")))!;
        Assert.Equal(ActivationHealthStatus.Degraded, summary.Status);
        Assert.Equal(1, summary.Passing);
        Assert.Equal(1, summary.Failing);

        var tenantFailing = await GetAsync($"/api/v1/admin/tenants/{tenantId}/health-checks?status=fail,error");
        Assert.Equal(HttpStatusCode.OK, tenantFailing.StatusCode);
        var failingChecks = (await ReadAsJsonAsync<List<AgentHealthCheck>>(tenantFailing))!;
        Assert.Equal(failing.Id, Assert.Single(failingChecks).Id);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await GetAsync($"/api/v1/admin/tenants/{tenantId}/health-checks?status=broken")).StatusCode);
    }

    /// <summary>Writes runs oldest first, the way the runner would, so the last status wins.</summary>
    private async Task SeedRunsAsync(string tenantId, AgentHealthCheck check, params HealthCheckStatus[] statuses)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAgentHealthCheckRepository>();
        var startedAt = DateTime.UtcNow.AddMinutes(-statuses.Length * 15);

        foreach (var status in statuses)
        {
            startedAt = startedAt.AddMinutes(15);
            await repository.InsertRunAsync(new AgentHealthCheckRun
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = tenantId,
                CheckId = check.Id,
                ActivationId = check.ActivationId,
                Type = check.Type,
                Status = status,
                StartedAt = startedAt,
                DurationMs = 120,
                Detail = $"seeded {status}"
            });
            await repository.RecordRunResultAsync(check.Id, status, startedAt, $"seeded {status}");
        }
    }
}
