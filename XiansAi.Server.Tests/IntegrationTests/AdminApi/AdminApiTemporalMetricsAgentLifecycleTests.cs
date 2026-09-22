using System.Net;
using Shared.Data.Models.Usage;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System Metrics agent authored with Xians.Lib. Chat reports via context.Metrics (handler /
/// activity HTTP) and via XiansContext.Metrics from a custom workflow (UsageActivities stub).
/// Admin stats / categories read those records; another tenant and agent do not.
/// See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/metrics/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalMetricsAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalMetricsAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task MetricsAgent_HandlerAndWorkflowReport_AdminReadsAndIsolates()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Metrics {Guid.NewGuid():N}";
        var otherAgentName = $"Metrics Other {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var participantId = $"reader-{Guid.NewGuid():N}@example.com";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var metricsAgent = RegisterMetricsAgent(host, agentName);
        var otherAgent = RegisterIsolationAgent(host, otherAgentName);
        await host.StartWorkersAsync(metricsAgent, otherAgent);
        await WaitForTemplateAsync(agentName);
        await WaitForTemplateAsync(otherAgentName);

        await DeployLibTemplateAsync(ownerTenant, agentName);
        await DeployLibTemplateAsync(otherTenant, agentName);
        await DeployLibTemplateAsync(ownerTenant, otherAgentName);

        var ownerActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, activationName);
        var otherTenantActivationId = await ActivateLibAgentAsync(otherTenant, agentName, activationName);
        var otherAgentActivationId = await ActivateLibAgentAsync(ownerTenant, otherAgentName, activationName);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, "reported:handler",
            userText: "handler", participantId: participantId);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, "reported:workflow",
            userText: "workflow", participantId: participantId);

        var stats = await WaitForStatsAsync(ownerTenant, agentName, expectedRecords: 5);
        Assert.Equal(150, TypeSum(stats, "tokens", "total"));
        Assert.Equal(45, TypeSum(stats, "tokens", "prompt"));
        Assert.Equal(105, TypeSum(stats, "tokens", "completion"));
        Assert.Equal(1, TypeSum(stats, "approvals", "submitted"));
        Assert.Equal(1, TypeSum(stats, "documents", "generated"));
        Assert.Contains(stats.ByActivation, item =>
            string.Equals(item.ActivationName, activationName, StringComparison.Ordinal));

        var categories = await GetCategoriesAsync(ownerTenant, agentName);
        Assert.Contains(categories.Categories, item =>
            string.Equals(item.Category, "tokens", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(categories.Categories, item =>
            string.Equals(item.Category, "approvals", StringComparison.OrdinalIgnoreCase));

        var modelStats = await GetStatsAsync(ownerTenant, agentName, model: "gpt-4");
        Assert.Equal(3, modelStats.Summary.TotalMetricRecords);
        Assert.Equal(150, TypeSum(modelStats, "tokens", "total"));

        var otherTenantStats = await GetStatsAsync(otherTenant, agentName);
        Assert.Equal(0, otherTenantStats.Summary.TotalMetricRecords);

        var otherAgentStats = await GetStatsAsync(ownerTenant, otherAgentName);
        Assert.Equal(0, otherAgentStats.Summary.TotalMetricRecords);

        BindTenantContext(ownerTenant, _adminUserId!);
        var delete = await DeleteAsync(
            $"/api/v1/admin/tenants/{ownerTenant}/metrics/agents/{Uri.EscapeDataString(agentName)}/activation/{Uri.EscapeDataString(activationName)}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var afterDelete = await GetStatsAsync(ownerTenant, agentName);
        Assert.Equal(0, afterDelete.Summary.TotalMetricRecords);

        await RemoveLibActivationAsync(ownerTenant, ownerActivationId);
        await RemoveLibActivationAsync(otherTenant, otherTenantActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherAgentActivationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibDeploymentAsync(otherTenant, agentName);
        await RemoveLibDeploymentAsync(ownerTenant, otherAgentName);
        await RemoveLibTemplateAsync(agentName);
        await RemoveLibTemplateAsync(otherAgentName);
    }

    private static XiansAgent RegisterMetricsAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Reports usage metrics from chat and from a workflow");
        agent.Workflows.DefineCustom<MetricsReportWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:MetricsReport");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleMetricsChatAsync);
        return agent;
    }

    private static XiansAgent RegisterIsolationAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Isolation agent that never reports metrics");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context => context.ReplyAsync("noop"));
        return agent;
    }

    private static async Task HandleMetricsChatAsync(Xians.Lib.Agents.Messaging.UserMessageContext context)
    {
        try
        {
            await context.ReplyAsync(await DispatchMetricsCommandAsync(context));
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private static async Task<string> DispatchMetricsCommandAsync(
        Xians.Lib.Agents.Messaging.UserMessageContext context)
    {
        var command = context.Message.Text?.Trim() ?? string.Empty;
        if (command == "handler")
        {
            await ReportHandlerMetricsAsync(context);
            return "reported:handler";
        }

        if (command == "workflow")
        {
            var agentName = XiansContext.SafeAgentName ?? XiansContext.CurrentAgent.Name;
            return await XiansContext.Workflows.ExecuteAsync<string>(
                $"{agentName}:MetricsReport",
                [],
                uniqueKey: Guid.NewGuid().ToString("N")[..8]);
        }

        return "unknown";
    }

    private static Task ReportHandlerMetricsAsync(Xians.Lib.Agents.Messaging.UserMessageContext context)
    {
        return context.Metrics
            .ForModel("gpt-4")
            .WithCustomIdentifier($"msg-{context.Message.RequestId}")
            .WithMetadata("version", "2.1.0")
            .WithMetrics(
                ("tokens", "prompt", 45, "tokens"),
                ("tokens", "completion", 105, "tokens"),
                ("tokens", "total", 150, "tokens"))
            .ReportAsync();
    }

    private async Task<AdminMetricsStatsResponse> WaitForStatsAsync(
        string tenantId,
        string agentName,
        int expectedRecords)
    {
        AdminMetricsStatsResponse? last = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            last = await GetStatsAsync(tenantId, agentName);
            if (last.Summary.TotalMetricRecords >= expectedRecords)
            {
                return last;
            }

            await Task.Delay(250);
        }

        var types = last == null
            ? "(no response)"
            : string.Join(", ", last.CategoriesAndTypes.Select(item =>
                $"{item.Category}:{string.Join("+", item.Types.Select(type => $"{type.Type}={type.Stats.Sum}"))}"));
        Assert.Fail(
            $"Metrics for {agentName} on {tenantId} did not reach {expectedRecords} records. " +
            $"Last count: {last?.Summary.TotalMetricRecords}. Types: {types}");
        return last!;
    }

    private async Task<AdminMetricsStatsResponse> GetStatsAsync(
        string tenantId,
        string agentName,
        string? model = null)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var start = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));
        var uri =
            $"/api/v1/admin/tenants/{tenantId}/metrics/stats" +
            $"?agentName={Uri.EscapeDataString(agentName)}" +
            $"&startDate={start}&endDate={end}";
        if (!string.IsNullOrWhiteSpace(model))
        {
            uri += $"&model={Uri.EscapeDataString(model)}";
        }

        var response = await GetAsync(uri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = await ReadAsJsonAsync<AdminMetricsStatsResponse>(response);
        Assert.NotNull(stats);
        return stats!;
    }

    private async Task<AdminMetricsCategoriesResponse> GetCategoriesAsync(string tenantId, string agentName)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/metrics/categories?agentName={Uri.EscapeDataString(agentName)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await ReadAsJsonAsync<AdminMetricsCategoriesResponse>(response);
        Assert.NotNull(categories);
        return categories!;
    }

    private static double TypeSum(AdminMetricsStatsResponse stats, string category, string type)
    {
        var match = stats.CategoriesAndTypes.FirstOrDefault(item =>
            string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(match);
        var typeMatch = match!.Types.FirstOrDefault(item =>
            string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(typeMatch);
        return typeMatch!.Stats.Sum;
    }
}

[Workflow("placeholder:MetricsReport")]
public class MetricsReportWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        await XiansContext.Metrics
            .WithMetrics(
                ("approvals", "submitted", 1, "count"),
                ("documents", "generated", 1, "count"))
            .ReportAsync();
        return "reported:workflow";
    }
}
