using System.Net;
using System.Text;
using System.Text.Json;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Webhook SDK DeleteAsync and WebhookResponse non-200 factories. Integrator 200 + Admin
/// delete stay on the Webhooks cycle. See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/webhooks/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalWebhookSdkAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalWebhookSdkAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task WebhookSdkAgent_DeleteAsync_AndNon200Response()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"HookSdk {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var hookName = $"Deny-{Guid.NewGuid():N}"[..12];
        var payload = "{\"event\":\"denied\"}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterWebhookSdkAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "created",
            userText: $"create {hookName}");

        BindTenantContext(tenantId, _adminUserId!);
        var listed = await ListAdminWebhooksAsync(tenantId, agentName);
        var hook = Assert.Single(listed);
        var url = hook.GetProperty("webhookUrl").GetString()!;

        using var inbound = _factory.CreateClient();
        inbound.DefaultRequestHeaders.Clear();
        var denied = await PostInboundAsync(inbound, url, payload);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        Assert.Contains("denied", deniedBody, StringComparison.Ordinal);

        BindTenantContext(tenantId, _adminUserId!);
        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "deleted:1",
            userText: "delete");

        BindTenantContext(tenantId, _adminUserId!);
        Assert.Empty(await ListAdminWebhooksAsync(tenantId, agentName));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostInboundAsync(inbound, url, payload)).StatusCode);

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterWebhookSdkAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "SDK delete and non-200 WebhookResponse");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleWebhookSdkChatAsync);

        var integrator = agent.Workflows.DefineIntegrator();
        integrator.OnWebhook(context =>
            context.Respond(WebhookResponse.NotFound("denied")));

        return agent;
    }

    private static async Task HandleWebhookSdkChatAsync(UserMessageContext context)
    {
        try
        {
            var parts = (context.Message.Text ?? string.Empty)
                .Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var webhooks = XiansContext.CurrentAgent.Webhooks;

            if (parts.Length == 2 && parts[0] == "create")
            {
                await webhooks.CreateAsync(webhookName: parts[1]);
                await context.ReplyAsync("created");
                return;
            }

            if (parts.Length == 1 && parts[0] == "delete")
            {
                var listed = await webhooks.ListAsync();
                var deleted = 0;
                foreach (var webhook in listed)
                {
                    if (await webhooks.DeleteAsync(webhook.Id))
                    {
                        deleted++;
                    }
                }

                await context.ReplyAsync($"deleted:{deleted}");
                return;
            }

            await context.ReplyAsync("unknown");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task<List<JsonElement>> ListAdminWebhooksAsync(string tenantId, string agentName)
    {
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/webhooks?agentName={Uri.EscapeDataString(agentName)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("webhooks").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static async Task<HttpResponseMessage> PostInboundAsync(
        HttpClient inbound,
        string webhookUrl,
        string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        return await inbound.SendAsync(request);
    }
}
