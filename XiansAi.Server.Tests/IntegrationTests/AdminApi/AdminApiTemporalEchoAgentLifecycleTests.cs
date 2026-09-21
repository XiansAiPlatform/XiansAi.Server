using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Auth;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Workflows;
using Xians.Lib.Common.Testing;
using Xians.Lib.Configuration.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Full Admin API cycle for a system Echo agent created with Xians.Lib
/// (the same shape as Xians.Examples/EchoAgent): upload template, deploy to a
/// tenant, activate, send a chat message, assert history plus live Admin SSE
/// and tenant SignalR ReceiveChat, then deactivate and remove.
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
        var encodedAgent = Uri.EscapeDataString(agentName);

        TestCleanup.ResetAllStaticState();
        WorkflowDefinitionUploader.ResetCache();

        await using var loopback = TestServerLoopback.Start(_factory.Server);
        CancellationTokenSource? workerCts = null;
        Task? workerTask = null;

        try
        {
            var platform = await CreatePlatformAsync(loopback.BaseAddress, tenantId);
            var echoAgent = RegisterEchoAgent(platform, agentName, isTemplate: true);
            workerCts = new CancellationTokenSource();
            workerTask = echoAgent.RunAllAsync(workerCts.Token);
            await WaitForWorkerAsync(workerTask);
            await WaitForTemplateAsync(encodedAgent);

            var deploy = await PostAsJsonAsync(
                $"/api/v1/admin/agentTemplates/by-name/{encodedAgent}/deploy?tenantId={Uri.EscapeDataString(tenantId)}",
                new { });
            Assert.Equal(HttpStatusCode.OK, deploy.StatusCode);

            var create = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations", new
            {
                name = activationName,
                agentName,
                participantId = _adminUserId
            });
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            using var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            var activationId = createdJson.RootElement.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(activationId));

            var activate = await PostAsJsonAsync(
                $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}/activate",
                new { });
            Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

            var expectedEcho = $"Echo: {userText}";
            var workflowId = $"{tenantId}:{agentName}:Supervisor Workflow:{activationName}";
            using var liveCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var sseClient = LiveEchoStreams.CreateStreamingClient(_factory, _adminApiKey!, tenantId);
            await using var sse = await LiveEchoStreams.ListenAdminAsync(
                sseClient, tenantId, agentName, activationName, participantId, liveCts.Token);
            await using var hub = await LiveEchoStreams.ConnectTenantChatHubAsync(
                _factory.Server, _adminApiKey!, tenantId, workflowId, liveCts.Token);
            BindTenantContext(tenantId, _adminUserId!);

            var send = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/send", new
            {
                agentName,
                activationName,
                participantId,
                text = userText
            });
            Assert.Equal(HttpStatusCode.OK, send.StatusCode);

            var (echoed, historyBody) = await WaitForHistoryAsync(
                tenantId, agentName, activationName, participantId, expectedEcho);
            var workerError = workerTask.IsFaulted ? workerTask.Exception?.GetBaseException().Message : "none";
            Assert.True(
                echoed,
                $"Echo reply did not appear in messaging history. Worker error: {workerError}. History: {historyBody}");

            try
            {
                await sse.WaitForTextAsync(expectedEcho, liveCts.Token);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Echo reply did not appear on Admin SSE. {ex.Message} Buffer: {sse.Buffer}");
            }

            try
            {
                await hub.WaitForTextAsync(expectedEcho, liveCts.Token);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Echo reply did not appear on SignalR ReceiveChat. {ex.Message} Payload: {hub.Received}");
            }

            var deactivate = await PostAsJsonAsync(
                $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}/deactivate",
                new { });
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

            var deleteActivation = await DeleteAsync(
                $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}");
            Assert.Equal(HttpStatusCode.OK, deleteActivation.StatusCode);

            var deleteDeployment = await DeleteAsync(
                $"/api/v1/admin/tenants/{tenantId}/agentDeployments/{encodedAgent}?forceDelete=true");
            Assert.Equal(HttpStatusCode.OK, deleteDeployment.StatusCode);

            var deleteTemplate = await DeleteAsync(
                $"/api/v1/admin/agentTemplates/by-name/{encodedAgent}?cleanActivations=true");
            Assert.Equal(HttpStatusCode.NoContent, deleteTemplate.StatusCode);
        }
        finally
        {
            if (workerCts != null)
            {
                workerCts.Cancel();
                if (workerTask != null)
                {
                    try
                    {
                        await workerTask.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch (Exception)
                    {
                        // Worker shutdown is best-effort; Temporal cancel races are expected.
                    }
                }

                workerCts.Dispose();
            }

            TestCleanup.ResetAllStaticState();
            WorkflowDefinitionUploader.ResetCache();
        }
    }

    private async Task<XiansPlatform> CreatePlatformAsync(string serverUrl, string tenantId)
    {
        return await XiansPlatform.InitializeAsync(new XiansOptions
        {
            ServerUrl = serverUrl,
            ApiKey = XiansLibTestCertificate.CreateApiKey(tenantId, _adminUserId ?? "test-admin"),
            ConsoleLogLevel = LogLevel.Warning,
            ServerLogLevel = LogLevel.None,
            EnableTasks = false,
            TemporalConfiguration = new TemporalConfiguration
            {
                ServerUrl = Temporal.TargetHost,
                Namespace = Temporal.Namespace
            }
        });
    }

    private static XiansAgent RegisterEchoAgent(XiansPlatform platform, string agentName, bool isTemplate)
    {
        var agent = platform.Agents.Register(new XiansAgentRegistration
        {
            Name = agentName,
            Description = "A simple conversational agent that echoes back user messages",
            SamplePrompts =
            [
                "Hello, Echo Agent!",
                "Echo this message back to me"
            ],
            IsTemplate = isTemplate,
            EnableTasks = false
        });

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            var userMessage = context.Message.Text ?? string.Empty;
            await context.ReplyAsync($"Echo: {userMessage}");
        });

        return agent;
    }

    private void BindTenantContext(string tenantId, string userId)
    {
        var tenantContext = _factory.Services.GetRequiredService<ITenantContext>();
        tenantContext.TenantId = tenantId;
        tenantContext.LoggedInUser = userId;
        tenantContext.ParticipantId = userId;
        tenantContext.UserRoles = [SystemRoles.SysAdmin, SystemRoles.TenantAdmin, SystemRoles.TenantUser];
        tenantContext.AuthorizedTenantIds = [tenantId];
    }

    private async Task WaitForTemplateAsync(string encodedAgent)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await GetAsync($"/api/v1/admin/agentTemplates/by-name/{encodedAgent}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("Xians.Lib did not upload the system Echo template in time.");
    }

    private static async Task WaitForWorkerAsync(Task workerTask)
    {
        var started = await Task.WhenAny(workerTask, Task.Delay(TimeSpan.FromSeconds(5)));
        if (started == workerTask)
        {
            await workerTask;
        }
    }

    private async Task<(bool Found, string LastBody)> WaitForHistoryAsync(
        string tenantId,
        string agentName,
        string activationName,
        string participantId,
        string expectedText)
    {
        var query =
            $"agentName={Uri.EscapeDataString(agentName)}" +
            $"&activationName={Uri.EscapeDataString(activationName)}" +
            $"&participantId={Uri.EscapeDataString(participantId)}";
        var lastBody = string.Empty;

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var history = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history?{query}");
            lastBody = await history.Content.ReadAsStringAsync();
            if (history.StatusCode == HttpStatusCode.OK &&
                lastBody.Contains(expectedText, StringComparison.Ordinal))
            {
                return (true, lastBody);
            }

            await Task.Delay(250);
        }

        return (false, lastBody);
    }
}
