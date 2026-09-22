using System.Net;
using System.Text;
using System.Text.Json;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System webhook agent authored with Xians.Lib. The Integrator handles inbound builtin
/// POSTs via OnWebhook; the supervisor creates webhooks through the SDK. Admin list/create
/// /delete round-trip those URLs. A POST to /api/user/webhooks/builtin authenticates with
/// apikeyId, waits for context.Respond, and is isolated by tenant and agent. Delete revokes
/// the key (401). Deactivate rejects later POSTs (409).
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalWebhookAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalWebhookAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task WebhookAgent_InboundBuiltin_AdminCrudIsolatesAndRevokes()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Webhooks {Guid.NewGuid():N}";
        var otherAgentName = $"Webhooks Other {Guid.NewGuid():N}";
        const string ownerActivation = "front-desk";
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var emailHook = $"EmailReceived-{suffix}";
        var invoiceHook = $"InvoicePaid-{suffix}";
        var trace = $"trace-{suffix}";
        var payload = "{\"event\":\"invoice-paid\",\"amount\":42}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var webhookAgent = RegisterWebhookAgent(host, agentName);
        var otherAgent = RegisterWebhookAgent(host, otherAgentName);
        await host.StartWorkersAsync(webhookAgent, otherAgent);
        await WaitForTemplateAsync(agentName);
        await WaitForTemplateAsync(otherAgentName);

        await DeployLibTemplateAsync(ownerTenant, agentName);
        await DeployLibTemplateAsync(otherTenant, agentName);
        await DeployLibTemplateAsync(ownerTenant, otherAgentName);

        var ownerActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, ownerActivation);
        var otherTenantActivationId = await ActivateLibAgentAsync(otherTenant, agentName, ownerActivation);
        var otherAgentActivationId = await ActivateLibAgentAsync(ownerTenant, otherAgentName, ownerActivation);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "created",
            userText: $"create {emailHook}");

        BindTenantContext(ownerTenant, _adminUserId!);
        var ownerListed = await ListAdminWebhooksAsync(ownerTenant, agentName);
        var emailWebhook = Assert.Single(ownerListed, item => NameOf(item) == emailHook);
        var emailUrl = UrlOf(emailWebhook);
        var emailId = IdOf(emailWebhook);

        using var inbound = _factory.CreateClient();
        inbound.DefaultRequestHeaders.Clear();

        var emailBody = await PostInboundAsync(inbound, emailUrl, payload, trace);
        AssertInbound(emailBody, agentName, ownerTenant, emailHook, payload, trace);

        BindTenantContext(otherTenant, _adminUserId!);
        var otherTenantList = await ListAdminWebhooksAsync(otherTenant, agentName);
        Assert.DoesNotContain(otherTenantList, item => IdOf(item) == emailId);

        var otherTenantCreated = await CreateAdminWebhookAsync(otherTenant, agentName, ownerActivation, emailHook);
        var otherTenantBody = await PostInboundAsync(inbound, UrlOf(otherTenantCreated), payload, trace);
        AssertInbound(otherTenantBody, agentName, otherTenant, emailHook, payload, trace);

        BindTenantContext(ownerTenant, _adminUserId!);
        var otherAgentCreated = await CreateAdminWebhookAsync(ownerTenant, otherAgentName, ownerActivation, emailHook);
        var otherAgentBody = await PostInboundAsync(inbound, UrlOf(otherAgentCreated), payload, trace);
        AssertInbound(otherAgentBody, otherAgentName, ownerTenant, emailHook, payload, trace);

        var unauthenticated = await inbound.PostAsync(
            "/api/user/webhooks/builtin?workflowName=Integrator%20Workflow&webhookName=x&agentName=x",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        BindTenantContext(ownerTenant, _adminUserId!);
        var invoiceCreated = await CreateAdminWebhookAsync(ownerTenant, agentName, ownerActivation, invoiceHook);
        var invoiceBody = await PostInboundAsync(inbound, UrlOf(invoiceCreated), payload, trace);
        AssertInbound(invoiceBody, agentName, ownerTenant, invoiceHook, payload, trace);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "2",
            userText: "list");

        BindTenantContext(ownerTenant, _adminUserId!);
        var delete = await DeleteAsync($"/api/v1/admin/tenants/{ownerTenant}/webhooks/{emailId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostInboundRawAsync(inbound, emailUrl, payload, trace)).StatusCode);

        var afterDelete = await ListAdminWebhooksAsync(ownerTenant, agentName);
        Assert.DoesNotContain(afterDelete, item => IdOf(item) == emailId);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "1",
            userText: "list");

        BindTenantContext(ownerTenant, _adminUserId!);
        var deactivate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{ownerTenant}/agentActivations/{ownerActivationId}/deactivate",
            new { });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostInboundRawAsync(inbound, UrlOf(invoiceCreated), payload, trace)).StatusCode);

        await RemoveLibActivationAsync(ownerTenant, ownerActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherAgentActivationId);
        await RemoveLibActivationAsync(otherTenant, otherTenantActivationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibDeploymentAsync(otherTenant, agentName);
        await RemoveLibDeploymentAsync(ownerTenant, otherAgentName);
        await RemoveLibTemplateAsync(agentName);
        await RemoveLibTemplateAsync(otherAgentName);
    }

    private static XiansAgent RegisterWebhookAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Receives builtin webhooks and manages them through the SDK");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            try
            {
                await context.ReplyAsync(await WebhookChatCommands.ExecuteAsync(context.Message.Text));
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"error: {ex.Message}");
            }
        });

        var integrator = agent.Workflows.DefineIntegrator();
        integrator.OnWebhook(context =>
        {
            context.Respond(JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["agent"] = agentName,
                ["tenant"] = context.Webhook.TenantId,
                ["webhook"] = context.Webhook.Name,
                ["payload"] = context.Webhook.Payload ?? string.Empty,
                ["trace"] = Header(context.Metadata, "X-Test-Trace")
            }));
        });

        return agent;
    }

    private async Task<JsonElement> CreateAdminWebhookAsync(
        string tenantId,
        string agentName,
        string activationName,
        string webhookName)
    {
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/webhooks", new
        {
            activationName,
            agentName,
            webhookName,
            timeoutInSeconds = 30
        });
        Assert.True(response.IsSuccessStatusCode, $"Create webhook failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task<List<JsonElement>> ListAdminWebhooksAsync(string tenantId, string agentName)
    {
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/webhooks?agentName={Uri.EscapeDataString(agentName)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("webhooks").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static async Task<string> PostInboundAsync(
        HttpClient inbound,
        string webhookUrl,
        string payload,
        string trace)
    {
        var response = await PostInboundRawAsync(inbound, webhookUrl, payload, trace);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> PostInboundRawAsync(
        HttpClient inbound,
        string webhookUrl,
        string payload,
        string trace)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-Test-Trace", trace);
        return await inbound.SendAsync(request);
    }

    private static void AssertInbound(
        string body,
        string agentName,
        string tenantId,
        string webhookName,
        string payload,
        string trace)
    {
        using var json = JsonDocument.Parse(body);
        Assert.Equal(agentName, json.RootElement.GetProperty("agent").GetString());
        Assert.Equal(tenantId, json.RootElement.GetProperty("tenant").GetString());
        Assert.Equal(webhookName, json.RootElement.GetProperty("webhook").GetString());
        Assert.Equal(payload, json.RootElement.GetProperty("payload").GetString());
        Assert.Equal(trace, json.RootElement.GetProperty("trace").GetString());
    }

    private static string IdOf(JsonElement webhook) => webhook.GetProperty("id").GetString()!;

    private static string UrlOf(JsonElement webhook) => webhook.GetProperty("webhookUrl").GetString()!;

    private static string NameOf(JsonElement webhook)
    {
        if (webhook.TryGetProperty("configuration", out var configuration) &&
            configuration.ValueKind == JsonValueKind.Object &&
            configuration.TryGetProperty("webhookName", out var name) &&
            name.ValueKind == JsonValueKind.String)
        {
            return name.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string Header(Dictionary<string, string>? metadata, string name)
    {
        if (metadata == null)
        {
            return string.Empty;
        }

        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return string.Empty;
    }

    private static class WebhookChatCommands
    {
        public static async Task<string> ExecuteAsync(string? text)
        {
            var parts = (text ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "unknown-command";
            }

            var webhooks = XiansContext.CurrentAgent.Webhooks;
            if (parts[0] == "create" && parts.Length == 2)
            {
                await webhooks.CreateAsync(webhookName: parts[1]);
                return "created";
            }

            if (parts[0] == "list")
            {
                return (await webhooks.ListAsync()).Count.ToString();
            }

            return "unknown-command";
        }
    }
}
