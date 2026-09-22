using System.Net;
using System.Text.Json;
using Shared.Services;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Documents.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System Document DB agent authored with Xians.Lib. The running supervisor saves Type+Key
/// JSON to the server; Admin list/get/update/create round-trip that data. Queries from chat
/// are auto-scoped to agent, activation, and participant, so other tenants, agents,
/// activations, and participants do not see the owner's document.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalDocumentDbAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalDocumentDbAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task DocumentDbAgent_SavePush_AdminReadModifyAdd_Isolates()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Documents {Guid.NewGuid():N}";
        var otherAgentName = $"Documents Other {Guid.NewGuid():N}";
        const string ownerActivation = "front-desk";
        const string otherActivation = "back-office";
        const string dataType = "user-profile";
        var suffix = Guid.NewGuid().ToString("N");
        var userKey = $"user-{suffix}";
        var adminKey = $"admin-{suffix}";
        var ownerParticipant = $"owner-{suffix}@example.com";
        var otherParticipant = $"other-{suffix}@example.com";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var documentsAgent = RegisterDocumentAgent(host, agentName);
        var otherAgent = RegisterDocumentAgent(host, otherAgentName);
        await host.StartWorkersAsync(documentsAgent, otherAgent);
        await WaitForTemplateAsync(agentName);
        await WaitForTemplateAsync(otherAgentName);

        await DeployLibTemplateAsync(ownerTenant, agentName);
        await DeployLibTemplateAsync(otherTenant, agentName);
        await DeployLibTemplateAsync(ownerTenant, otherAgentName);

        var ownerActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, ownerActivation);
        var otherTenantActivationId = await ActivateLibAgentAsync(otherTenant, agentName, ownerActivation);
        var otherAgentActivationId = await ActivateLibAgentAsync(ownerTenant, otherAgentName, ownerActivation);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "saved",
            userText: Command("save", dataType, userKey, "gold"),
            participantId: ownerParticipant);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "gold",
            userText: Command("get", dataType, userKey),
            participantId: ownerParticipant);

        BindTenantContext(ownerTenant, _adminUserId!);
        var listed = await ListAdminDocumentsAsync(ownerTenant, agentName, dataType);
        var stored = Assert.Single(listed.Data, item => item.Key == userKey);
        Assert.Equal(dataType, stored.Type);
        Assert.Equal(agentName, stored.AgentName);
        Assert.Equal(ownerActivation, stored.ActivationName);
        Assert.Equal(ownerParticipant, stored.ParticipantId);
        Assert.Equal("gold", PlanOf(stored.Content));

        var schema = await GetAdminSchemaAsync(ownerTenant, agentName);
        Assert.Contains(dataType, schema.Types);

        var getById = await GetAsync($"/api/v1/admin/tenants/{ownerTenant}/data/{stored.Id}");
        Assert.Equal(HttpStatusCode.OK, getById.StatusCode);
        var fetched = await ReadAsJsonAsync<AdminDataItemResponse>(getById);
        Assert.Equal("gold", PlanOf(fetched!.Content));

        var update = await PutAsJsonAsync(
            $"/api/v1/admin/tenants/{ownerTenant}/data/{stored.Id}",
            new { content = new { plan = "platinum" } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "platinum",
            userText: Command("get", dataType, userKey),
            participantId: ownerParticipant);

        await AssertAgentRepliesWithAsync(
            otherTenant, agentName, ownerActivation, "missing",
            userText: Command("get", dataType, userKey));
        await AssertAgentRepliesWithAsync(
            ownerTenant, otherAgentName, ownerActivation, "missing",
            userText: Command("get", dataType, userKey));
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "missing",
            userText: Command("get", dataType, userKey),
            participantId: otherParticipant);

        var otherActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, otherActivation);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, otherActivation, "missing",
            userText: Command("get", dataType, userKey));

        BindTenantContext(ownerTenant, _adminUserId!);
        var adminCreate = await PostAsJsonAsync($"/api/v1/admin/tenants/{ownerTenant}/data", new
        {
            agentName,
            dataType,
            key = adminKey,
            activationName = ownerActivation,
            participantId = ownerParticipant,
            content = new { plan = "bronze" }
        });
        Assert.Equal(HttpStatusCode.Created, adminCreate.StatusCode);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "bronze",
            userText: Command("get", dataType, adminKey),
            participantId: ownerParticipant);

        await RemoveLibActivationAsync(ownerTenant, ownerActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherAgentActivationId);
        await RemoveLibActivationAsync(otherTenant, otherTenantActivationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibDeploymentAsync(otherTenant, agentName);
        await RemoveLibDeploymentAsync(ownerTenant, otherAgentName);
        await RemoveLibTemplateAsync(agentName);
        await RemoveLibTemplateAsync(otherAgentName);
    }

    private static XiansAgent RegisterDocumentAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Saves and reads Document DB Type+Key records");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            try
            {
                await context.ReplyAsync(await DocumentChatCommands.ExecuteAsync(context.Message.Text));
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"error: {ex.Message}");
            }
        });

        return agent;
    }

    private async Task<AdminDataListResponse> ListAdminDocumentsAsync(string tenantId, string agentName, string dataType)
    {
        var response = await GetAsync(AdminDataRangeUrl(tenantId, "data", agentName, dataType));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsJsonAsync<AdminDataListResponse>(response);
        Assert.NotNull(list);
        return list!;
    }

    private async Task<AdminDataSchemaResponse> GetAdminSchemaAsync(string tenantId, string agentName)
    {
        var response = await GetAsync(AdminDataRangeUrl(tenantId, "data/schema", agentName));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var schema = await ReadAsJsonAsync<AdminDataSchemaResponse>(response);
        Assert.NotNull(schema);
        return schema!;
    }

    private static string AdminDataRangeUrl(string tenantId, string path, string agentName, string? dataType = null)
    {
        var start = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));
        var url =
            $"/api/v1/admin/tenants/{tenantId}/{path}" +
            $"?startDate={start}&endDate={end}&agentName={Uri.EscapeDataString(agentName)}";
        if (!string.IsNullOrEmpty(dataType))
        {
            url += $"&dataType={Uri.EscapeDataString(dataType)}";
        }

        return url;
    }

    private static string Command(string verb, string type, string key, string? plan = null)
    {
        return plan == null ? $"{verb} {type} {key}" : $"{verb} {type} {key} {plan}";
    }

    private static string PlanOf(JsonElement? content)
    {
        if (content is JsonElement element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("plan", out var plan) &&
            plan.ValueKind == JsonValueKind.String)
        {
            return plan.GetString() ?? "missing";
        }

        return "missing";
    }

    private static class DocumentChatCommands
    {
        public static async Task<string> ExecuteAsync(string? text)
        {
            var parts = (text ?? string.Empty).Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return "unknown-command";
            }

            var command = parts[0];
            var type = parts[1];
            var key = parts[2];
            var documents = XiansContext.CurrentAgent.Documents;

            if (command == "save" && parts.Length == 4)
            {
                await documents.SaveAsync(new Document
                {
                    Type = type,
                    Key = key,
                    Content = JsonSerializer.SerializeToElement(new { plan = parts[3] })
                });
                return "saved";
            }

            if (command == "get")
            {
                var document = await documents.GetByKeyAsync(type, key);
                return PlanOf(document?.Content);
            }

            return "unknown-command";
        }
    }
}
