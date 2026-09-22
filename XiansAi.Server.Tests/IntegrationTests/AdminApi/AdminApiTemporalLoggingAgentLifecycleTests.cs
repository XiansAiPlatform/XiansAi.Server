using System.Net;
using Microsoft.Extensions.Logging;
using Shared.Services;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Workflows.Models;
using Xians.Lib.Logging;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System Logging agent authored with Xians.Lib. Chat logs from an activity via
/// XiansLogger.GetLogger and from a workflow via Workflow.Logger. Admin streams/logs
/// read those rows; another tenant and agent do not. See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/logging/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalLoggingAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalLoggingAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task LoggingAgent_WorkflowAndActivityLogs_AdminReadsAndIsolates()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Logging {Guid.NewGuid():N}";
        var otherAgentName = $"Logging Other {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var participantId = $"reader-{Guid.NewGuid():N}@example.com";
        var token = Guid.NewGuid().ToString("N")[..8];
        var activityMessage = $"activity-log:{token}";
        var activityWarning = $"activity-warn:{token}";
        var workflowMessage = $"workflow-log:{token}";
        var supervisorWorkflowId = $"{ownerTenant}:{agentName}:Supervisor Workflow:{activationName}";
        var probeWorkflowId = $"{ownerTenant}:{agentName}:LogProbe:{activationName}:{token}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!,
            consoleLogLevel: LogLevel.Information,
            serverLogLevel: LogLevel.Information);

        var loggingAgent = RegisterLoggingAgent(host, agentName);
        var otherAgent = RegisterIsolationAgent(host, otherAgentName);
        await host.StartWorkersAsync(loggingAgent, otherAgent);
        await WaitForTemplateAsync(agentName);
        await WaitForTemplateAsync(otherAgentName);

        await DeployLibTemplateAsync(ownerTenant, agentName);
        await DeployLibTemplateAsync(otherTenant, agentName);
        await DeployLibTemplateAsync(ownerTenant, otherAgentName);

        var ownerActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, activationName);
        var otherTenantActivationId = await ActivateLibAgentAsync(otherTenant, agentName, activationName);
        var otherAgentActivationId = await ActivateLibAgentAsync(ownerTenant, otherAgentName, activationName);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, $"logged:activity:{token}",
            userText: $"activity {token}", participantId: participantId);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, $"logged:workflow:{token}",
            userText: $"workflow {token}", participantId: participantId);

        var activityLogs = await WaitForLogContainingAsync(
            ownerTenant, agentName, activityMessage, workflowId: supervisorWorkflowId);
        Assert.Contains(activityLogs.Logs, log => log.Message.Contains(activityWarning, StringComparison.Ordinal));
        Assert.Contains(activityLogs.Logs, log =>
            string.Equals(log.Activation, activationName, StringComparison.Ordinal));

        var workflowLogs = await WaitForLogContainingAsync(
            ownerTenant, agentName, workflowMessage, workflowId: probeWorkflowId);
        Assert.Contains(workflowLogs.Logs, log =>
            string.Equals(log.WorkflowId, probeWorkflowId, StringComparison.Ordinal));

        var streams = await GetStreamsAsync(ownerTenant, agentName);
        Assert.Contains(streams.Streams, stream => stream.WorkflowId == supervisorWorkflowId);
        Assert.Contains(streams.Streams, stream => stream.WorkflowId == probeWorkflowId);

        var warnings = await GetLogsAsync(ownerTenant, agentName, logLevel: "Warning");
        Assert.Contains(warnings.Logs, log => log.Message.Contains(activityWarning, StringComparison.Ordinal));
        Assert.DoesNotContain(warnings.Logs, log => log.Message.Contains(activityMessage, StringComparison.Ordinal));
        Assert.DoesNotContain(warnings.Logs, log => log.Message.Contains(workflowMessage, StringComparison.Ordinal));

        var otherTenantLogs = await GetLogsAsync(otherTenant, agentName);
        Assert.DoesNotContain(otherTenantLogs.Logs, log => log.Message.Contains(token, StringComparison.Ordinal));

        var otherAgentLogs = await GetLogsAsync(ownerTenant, otherAgentName);
        Assert.DoesNotContain(otherAgentLogs.Logs, log => log.Message.Contains(token, StringComparison.Ordinal));

        BindTenantContext(ownerTenant, _adminUserId!);
        var delete = await DeleteAsync(
            $"/api/v1/admin/tenants/{ownerTenant}/logs/agents/{Uri.EscapeDataString(agentName)}/activation/{Uri.EscapeDataString(activationName)}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var afterDelete = await GetLogsAsync(ownerTenant, agentName);
        Assert.DoesNotContain(afterDelete.Logs, log => log.Message.Contains(token, StringComparison.Ordinal));

        await RemoveLibActivationAsync(ownerTenant, ownerActivationId);
        await RemoveLibActivationAsync(otherTenant, otherTenantActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherAgentActivationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibDeploymentAsync(otherTenant, agentName);
        await RemoveLibDeploymentAsync(ownerTenant, otherAgentName);
        await RemoveLibTemplateAsync(agentName);
        await RemoveLibTemplateAsync(otherAgentName);
    }

    private static XiansAgent RegisterLoggingAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Logs from chat (activity) and from a workflow");
        agent.Workflows.DefineCustom<LoggingProbeWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:LogProbe");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleLoggingChatAsync);
        return agent;
    }

    private static XiansAgent RegisterIsolationAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Isolation agent that never logs probe messages");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context => context.ReplyAsync("noop"));
        return agent;
    }

    private static async Task HandleLoggingChatAsync(Xians.Lib.Agents.Messaging.UserMessageContext context)
    {
        try
        {
            await context.ReplyAsync(await DispatchLoggingCommandAsync(context));
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private static async Task<string> DispatchLoggingCommandAsync(
        Xians.Lib.Agents.Messaging.UserMessageContext context)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        var space = text.IndexOf(' ');
        var command = space < 0 ? text : text[..space];
        var token = space < 0 ? string.Empty : text[(space + 1)..].Trim();

        if (command == "activity")
        {
            var logger = XiansLogger.GetLogger<AdminApiTemporalLoggingAgentLifecycleTests>();
            logger.LogInformation("activity-log:{Token}", token);
            logger.LogWarning("activity-warn:{Token}", token);
            return $"logged:activity:{token}";
        }

        if (command == "workflow")
        {
            var agentName = XiansContext.SafeAgentName ?? XiansContext.CurrentAgent.Name;
            return await XiansContext.Workflows.ExecuteAsync<string>(
                $"{agentName}:LogProbe",
                [token],
                uniqueKey: token);
        }

        return "unknown";
    }

    private async Task<AdminLogsResponse> WaitForLogContainingAsync(
        string tenantId,
        string agentName,
        string expectedText,
        string? workflowId = null)
    {
        AdminLogsResponse? last = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            last = await GetLogsAsync(tenantId, agentName, workflowId: workflowId);
            if (last.Logs.Any(log => log.Message.Contains(expectedText, StringComparison.Ordinal)))
            {
                return last;
            }

            await Task.Delay(250);
        }

        var sample = last == null
            ? "(no response)"
            : string.Join(" | ", last.Logs.Take(8).Select(log => log.Message));
        Assert.Fail(
            $"Log '{expectedText}' for {agentName} on {tenantId} did not appear. " +
            $"Last count: {last?.TotalCount}. Sample: {sample}");
        return last!;
    }

    private async Task<AdminLogsResponse> GetLogsAsync(
        string tenantId,
        string agentName,
        string? workflowId = null,
        string? logLevel = null)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri =
            $"/api/v1/admin/tenants/{tenantId}/logs" +
            $"?agentName={Uri.EscapeDataString(agentName)}" +
            "&pageSize=100";
        if (!string.IsNullOrWhiteSpace(workflowId))
        {
            uri += $"&workflowId={Uri.EscapeDataString(workflowId)}";
        }

        if (!string.IsNullOrWhiteSpace(logLevel))
        {
            uri += $"&logLevel={Uri.EscapeDataString(logLevel)}";
        }

        var response = await GetAsync(uri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logs = await ReadAsJsonAsync<AdminLogsResponse>(response);
        Assert.NotNull(logs);
        return logs!;
    }

    private async Task<AdminLogStreamsResponse> GetStreamsAsync(string tenantId, string agentName)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs/streams" +
            $"?agentName={Uri.EscapeDataString(agentName)}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var streams = await ReadAsJsonAsync<AdminLogStreamsResponse>(response);
        Assert.NotNull(streams);
        return streams!;
    }
}

[Workflow("placeholder:LogProbe")]
public class LoggingProbeWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string token)
    {
        Workflow.Logger.LogInformation("workflow-log:{Token}", token);
        return Task.FromResult($"logged:workflow:{token}");
    }
}
