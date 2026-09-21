using System.Net;
using System.Text.Json;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Remaining documented messaging SDK surface: inbound Data, handler progress
/// (reasoning/tool), proactive SendChatAsSupervisorAsync from a custom workflow,
/// and GetChatHistoryAsync scope isolation. Chat ReplyAsync and live Chat SSE
/// stay on Echo. See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/messaging-replying/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalMessagingSdkAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalMessagingSdkAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task MessagingSdkAgent_DataProgressProactiveAndHistoryIsolate()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"Messaging {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        const string alertsTopic = "alerts";
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sku = $"sku-{suffix}";
        var progressMarker = $"progress-{suffix}";
        var notifyMarker = $"notify-{suffix}";
        var defaultMarker = $"default-{suffix}";
        var alertsMarker = $"alerts-{suffix}";
        var participantId = $"owner-{suffix}@example.com";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterMessagingAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        BindTenantContext(tenantId, _adminUserId!);
        var sendData = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/send", new
        {
            agentName,
            activationName,
            participantId,
            text = "process",
            type = "Data",
            data = new { sku }
        });
        Assert.Equal(HttpStatusCode.OK, sendData.StatusCode);
        var (dataFound, dataHistory) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, $"data-ok:{sku}");
        Assert.True(dataFound, $"Data reply did not appear. History: {dataHistory}");
        Assert.True(
            HistoryHasOutgoing(dataHistory, "Data", $"data-ok:{sku}"),
            $"Expected outgoing Data message. History: {dataHistory}");

        using var liveCts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var adminSseClient = LiveEchoStreams.CreateStreamingClient(_factory, _adminApiKey!, tenantId);
        await using var adminSse = await LiveEchoStreams.ListenAdminAsync(
            adminSseClient, tenantId, agentName, activationName, participantId, liveCts.Token);
        BindTenantContext(tenantId, _adminUserId!);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"done:{progressMarker}",
            userText: $"progress {progressMarker}",
            participantId: participantId);
        var (progressFound, progressHistory) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, $"done:{progressMarker}");
        Assert.True(progressFound, $"Progress chat reply did not appear. History: {progressHistory}");
        Assert.True(
            HistoryHasOutgoing(progressHistory, "Reasoning", $"thinking:{progressMarker}"),
            $"Expected outgoing Reasoning. History: {progressHistory}");
        Assert.True(
            HistoryHasOutgoing(progressHistory, "Tool", $"tool:{progressMarker}"),
            $"Expected outgoing Tool. History: {progressHistory}");
        try
        {
            await adminSse.WaitForTextAsync($"thinking:{progressMarker}", liveCts.Token);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Reasoning did not appear on Admin SSE. {ex.Message} Payload: {adminSse.Buffer}");
        }

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"notified:{notifyMarker}",
            userText: $"notify {notifyMarker}",
            participantId: participantId);
        var (proactiveFound, proactiveHistory) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, $"proactive:{notifyMarker}");
        Assert.True(proactiveFound, $"Proactive supervisor chat did not appear. History: {proactiveHistory}");

        await SendChatAsync(
            tenantId, agentName, activationName, participantId, $"seed {defaultMarker}");
        var (seededDefault, _) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, $"seeded:{defaultMarker}");
        Assert.True(seededDefault, "Default-scope seed reply did not appear.");

        await SendChatAsync(
            tenantId, agentName, activationName, participantId, $"seed {alertsMarker}", alertsTopic);
        var (seededAlerts, _) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, $"seeded:{alertsMarker}", alertsTopic);
        Assert.True(seededAlerts, "Alerts-scope seed reply did not appear.");

        await SendChatAsync(
            tenantId, agentName, activationName, participantId, $"history {defaultMarker}", alertsTopic);
        var (missedDefault, missHistory) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, $"history-miss:{defaultMarker}", alertsTopic);
        Assert.True(missedDefault, $"Alerts history leaked default-scope text. History: {missHistory}");

        await SendChatAsync(
            tenantId, agentName, activationName, participantId, $"history {alertsMarker}", alertsTopic);
        var (hitAlerts, hitHistory) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, $"history-hit:{alertsMarker}", alertsTopic);
        Assert.True(hitAlerts, $"Alerts history missed same-scope text. History: {hitHistory}");

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterMessagingAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Covers data, progress, proactive, and chat history");
        agent.Workflows.DefineCustom<MessagingNotifyWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Notify");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleMessagingChatAsync);
        supervisor.OnUserDataMessage(HandleMessagingDataAsync);
        return agent;
    }

    private static async Task HandleMessagingChatAsync(UserMessageContext context)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var space = text.IndexOf(' ');
            var command = space < 0 ? text : text[..space];
            var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

            if (command == "progress")
            {
                await context.SendReasoningAsync(new { step = "thinking" }, $"thinking:{argument}");
                await context.SendToolExecAsync(new { tool = "lookup" }, $"tool:{argument}");
                await context.ReplyAsync($"done:{argument}");
                return;
            }

            if (command == "notify")
            {
                var agentName = XiansContext.SafeAgentName ?? XiansContext.CurrentAgent.Name;
                await XiansContext.Workflows.ExecuteAsync<string>(
                    $"{agentName}:Notify",
                    [context.Message.ParticipantId, argument],
                    uniqueKey: argument);
                await context.ReplyAsync($"notified:{argument}");
                return;
            }

            if (command == "seed")
            {
                await context.ReplyAsync($"seeded:{argument}");
                return;
            }

            if (command == "history")
            {
                var history = await context.GetChatHistoryAsync(1, 50);
                var found = history.Any(message =>
                    message.Text?.Contains(argument, StringComparison.Ordinal) == true);
                await context.ReplyAsync(found ? $"history-hit:{argument}" : $"history-miss:{argument}");
                return;
            }

            await context.ReplyAsync("unknown");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private static async Task HandleMessagingDataAsync(UserMessageContext context)
    {
        try
        {
            var sku = ReadSku(context.Message.Data);
            await context.SendDataAsync(new { sku, status = "processed" }, $"data-ok:{sku}");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task SendChatAsync(
        string tenantId,
        string agentName,
        string activationName,
        string participantId,
        string text,
        string? topic = null)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var send = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/send", new
        {
            agentName,
            activationName,
            participantId,
            text,
            topic
        });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
    }

    private static bool HistoryHasOutgoing(string historyJson, string messageType, string textContains)
    {
        using var json = JsonDocument.Parse(historyJson);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in json.RootElement.EnumerateArray())
        {
            var direction = ReadString(item, "direction");
            var type = ReadString(item, "messageType");
            var text = ReadString(item, "text");
            if (string.Equals(direction, "Outgoing", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(type, messageType, StringComparison.OrdinalIgnoreCase) &&
                text.Contains(textContains, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ReadSku(object? data)
    {
        if (data is JsonElement element)
        {
            return ReadSkuFromElement(element);
        }

        if (data is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (data == null)
        {
            return "missing";
        }

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
        return ReadSkuFromElement(json.RootElement);
    }

    private static string ReadSkuFromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("sku", out var sku) &&
            sku.ValueKind == JsonValueKind.String)
        {
            return sku.GetString() ?? "missing";
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? "missing"
            : "missing";
    }
}

[Workflow("placeholder:Notify")]
public class MessagingNotifyWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string participantId, string marker)
    {
        await XiansContext.Messaging.SendChatAsSupervisorAsync(
            $"proactive:{marker}",
            participantId: participantId);
        return $"proactive:{marker}";
    }
}
