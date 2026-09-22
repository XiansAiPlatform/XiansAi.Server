using System.Text.Json;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Documents.Models;
using Xians.Lib.Agents.Messaging;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Document DB collection methods not covered by Save/GetByKey: Query, Get by id,
/// Update, Exists, and Delete. Auto-scoped Query is the extra contract.
/// See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/document-db/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalDocumentDbSdkAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalDocumentDbSdkAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task DocumentDbSdkAgent_QueryGetUpdateExistsDelete()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"DocSdk {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var participantId = $"owner-{Guid.NewGuid():N}@example.com";
        var otherParticipant = $"other-{Guid.NewGuid():N}@example.com";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterDocumentSdkAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "run:ok",
            userText: "run",
            participantId: participantId);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "saved",
            userText: "save leftover gold",
            participantId: participantId);
        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "query:0",
            userText: "query leftover",
            participantId: otherParticipant);

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterDocumentSdkAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Document DB Query/Get/Update/Exists/Delete");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleDocumentSdkChatAsync);
        return agent;
    }

    private static async Task HandleDocumentSdkChatAsync(UserMessageContext context)
    {
        try
        {
            await context.ReplyAsync(await DispatchAsync(context.Message.Text));
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private static async Task<string> DispatchAsync(string? text)
    {
        var command = (text ?? string.Empty).Trim();
        var documents = XiansContext.CurrentAgent.Documents;
        const string type = "sdk-profile";

        if (command == "run")
        {
            var gold = await documents.SaveAsync(new Document
            {
                Type = type,
                Key = "gold-key",
                Content = JsonSerializer.SerializeToElement(new { plan = "gold" })
            });
            var silver = await documents.SaveAsync(new Document
            {
                Type = type,
                Key = "silver-key",
                Content = JsonSerializer.SerializeToElement(new { plan = "silver" })
            });

            var listed = await documents.QueryAsync(new DocumentQuery { Type = type, Limit = 10 });
            if (listed.Count != 2)
            {
                return $"run:query-count:{listed.Count}";
            }

            var fetched = await documents.GetAsync(gold.Id!);
            if (PlanOf(fetched?.Content) != "gold")
            {
                return "run:get-mismatch";
            }

            if (!await documents.ExistsAsync(gold.Id!))
            {
                return "run:exists-false";
            }

            gold.Content = JsonSerializer.SerializeToElement(new { plan = "platinum" });
            if (!await documents.UpdateAsync(gold))
            {
                return "run:update-failed";
            }

            var updated = await documents.GetAsync(gold.Id!);
            if (PlanOf(updated?.Content) != "platinum")
            {
                return "run:update-mismatch";
            }

            if (!await documents.DeleteAsync(gold.Id!) || await documents.ExistsAsync(gold.Id!))
            {
                return "run:delete-failed";
            }

            var deletedCount = await documents.DeleteManyAsync([silver.Id!]);
            var remaining = await documents.QueryAsync(new DocumentQuery { Type = type, Limit = 10 });
            if (deletedCount != 1 || remaining.Count != 0)
            {
                return $"run:deletemany:{deletedCount}:{remaining.Count}";
            }

            return "run:ok";
        }

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[0] == "save")
        {
            await documents.SaveAsync(new Document
            {
                Type = parts[1],
                Key = parts[1],
                Content = JsonSerializer.SerializeToElement(new { plan = parts[2] })
            });
            return "saved";
        }

        if (parts.Length == 2 && parts[0] == "query")
        {
            var listed = await documents.QueryAsync(new DocumentQuery { Type = parts[1], Limit = 10 });
            return $"query:{listed.Count}";
        }

        return "unknown";
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
}
