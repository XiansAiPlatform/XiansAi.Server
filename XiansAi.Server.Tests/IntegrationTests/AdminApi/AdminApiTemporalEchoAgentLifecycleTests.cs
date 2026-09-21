using System.Net;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Full Admin API cycle for a system Echo agent created with Xians.Lib
/// (the same shape as Xians.Examples/EchoAgent): upload template, deploy to a
/// tenant, activate, send a chat message, assert history plus live Admin SSE,
/// UserApi SSE, tenant SignalR, and ChatHub ReceiveChat, then deactivate and remove.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalEchoAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalEchoAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task EchoAgent_TemplateDeployActivateMessageDeactivateAndRemove()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"Echo {Guid.NewGuid():N}";
        var activationName = "front-desk";
        var participantId = "reader@example.com";
        var userText = "hello from echo cycle";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var echoAgent = host.RegisterTemplate(
            agentName,
            "A simple conversational agent that echoes back user messages",
            ["Hello, Echo Agent!", "Echo this message back to me"]);
        var supervisor = echoAgent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            var userMessage = context.Message.Text ?? string.Empty;
            await context.ReplyAsync($"Echo: {userMessage}");
        });

        await host.StartWorkersAsync(echoAgent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        var expectedEcho = $"Echo: {userText}";
        var workflowId = $"{tenantId}:{agentName}:Supervisor Workflow:{activationName}";
        using var liveCts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var adminSseClient = LiveEchoStreams.CreateStreamingClient(_factory, _adminApiKey!, tenantId);
        var userSseClient = LiveEchoStreams.CreateStreamingClient(_factory, _adminApiKey!, tenantId);
        await using var adminSse = await LiveEchoStreams.ListenAdminAsync(
            adminSseClient, tenantId, agentName, activationName, participantId, liveCts.Token);
        await using var userSse = await LiveEchoStreams.ListenUserApiAsync(
            userSseClient, tenantId, workflowId, participantId, liveCts.Token);
        await using var tenantHub = await LiveEchoStreams.ConnectTenantChatHubAsync(
            _factory.Server, _adminApiKey!, tenantId, workflowId, liveCts.Token);
        await using var chatHub = await LiveEchoStreams.ConnectChatHubAsync(
            _factory.Server, _adminApiKey!, tenantId, workflowId, participantId, liveCts.Token);
        BindTenantContext(tenantId, _adminUserId!);

        var send = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/send", new
        {
            agentName,
            activationName,
            participantId,
            text = userText
        });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var (echoed, historyBody) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, expectedEcho);
        Assert.True(echoed, $"Echo reply did not appear in messaging history. History: {historyBody}");

        await AssertLiveTextAsync(
            "Admin SSE",
            ct => adminSse.WaitForTextAsync(expectedEcho, ct),
            () => adminSse.Buffer,
            liveCts.Token);
        await AssertLiveTextAsync(
            "UserApi SSE",
            ct => userSse.WaitForTextAsync(expectedEcho, ct),
            () => userSse.Buffer,
            liveCts.Token);
        await AssertLiveTextAsync(
            "tenant SignalR /ws/tenant/chat",
            ct => tenantHub.WaitForTextAsync(expectedEcho, ct),
            () => tenantHub.Received,
            liveCts.Token);
        await AssertLiveTextAsync(
            "ChatHub /ws/chat",
            ct => chatHub.WaitForTextAsync(expectedEcho, ct),
            () => chatHub.Received,
            liveCts.Token);

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static async Task AssertLiveTextAsync(
        string channel,
        Func<CancellationToken, Task> wait,
        Func<string> dump,
        CancellationToken cancellationToken)
    {
        try
        {
            await wait(cancellationToken);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Echo reply did not appear on {channel}. {ex.Message} Payload: {dump()}");
        }
    }
}
