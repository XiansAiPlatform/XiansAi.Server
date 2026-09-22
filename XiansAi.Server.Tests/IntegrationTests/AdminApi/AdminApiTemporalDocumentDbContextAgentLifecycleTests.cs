using System.Text.Json;
using Temporalio.Activities;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Documents.Models;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Document SaveAsync / GetByKeyAsync from a Temporal activity and from workflow code
/// (Lib stubs HTTP to DocumentActivities). Admin CRUD and Query/Exists stay on the other
/// Document DB cycles. See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/document-db/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalDocumentDbContextAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalDocumentDbContextAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public Task DocumentDbContextAgent_SaveGetByKey_FromActivity()
        => RunSaveGetCycleAsync(fromWorkflow: false);

    [Fact]
    public Task DocumentDbContextAgent_SaveGetByKey_FromWorkflow()
        => RunSaveGetCycleAsync(fromWorkflow: true);

    private async Task RunSaveGetCycleAsync(bool fromWorkflow)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"DocCtx {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var expectedRun = fromWorkflow ? "run:ok:workflow" : "run:ok:activity";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterDocumentContextAgent(host, agentName, fromWorkflow);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, expectedRun, userText: "run");

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterDocumentContextAgent(
        LibAgentWorkflowHost host,
        string agentName,
        bool fromWorkflow)
    {
        var agent = host.RegisterTemplate(agentName, "Save/GetByKey documents from activity and workflow");
        var manage = agent.Workflows.DefineCustom<DocumentDbContextManageWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Manage");
        manage.AddActivity(new DocumentDbContextActivities());

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context => HandleDocumentContextChatAsync(context, fromWorkflow));
        return agent;
    }

    private static async Task HandleDocumentContextChatAsync(UserMessageContext context, bool fromWorkflow)
    {
        try
        {
            var result = await XiansContext.Workflows.ExecuteAsync<DocumentDbContextManageWorkflow, string>(
                new object[] { context.Message.Text?.Trim() ?? string.Empty, fromWorkflow },
                uniqueKey: Guid.NewGuid().ToString("N"));
            await context.ReplyAsync(result);
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }
}

[Workflow("placeholder:DocCtxManage")]
public class DocumentDbContextManageWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string command, bool fromWorkflow)
    {
        if (fromWorkflow)
        {
            return DocumentDbContextDispatch.RunAsync("workflow");
        }

        return Workflow.ExecuteActivityAsync(
            (DocumentDbContextActivities activities) => activities.DispatchAsync(command),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    }
}

public class DocumentDbContextActivities
{
    [Activity]
    public Task<string> DispatchAsync(string command)
    {
        return DocumentDbContextDispatch.RunAsync("activity");
    }
}

internal static class DocumentDbContextDispatch
{
    private const string Type = "ctx-profile";
    private const string Key = "ctx-user";

    public static async Task<string> RunAsync(string context)
    {
        var documents = XiansContext.CurrentAgent.Documents;
        await documents.SaveAsync(new Document
        {
            Type = Type,
            Key = Key,
            Content = JsonSerializer.SerializeToElement(new { plan = "gold", ctx = context })
        });

        var fetched = await documents.GetByKeyAsync(Type, Key);
        if (fetched?.Content is not JsonElement element ||
            !element.TryGetProperty("plan", out var plan) ||
            plan.GetString() != "gold")
        {
            return $"run:get-mismatch:{context}";
        }

        var listed = await documents.QueryAsync(new DocumentQuery { Type = Type, Limit = 10 });
        if (listed.Count != 1)
        {
            return $"run:query-count:{context}:{listed.Count}";
        }

        if (!await documents.DeleteAsync(fetched.Id!))
        {
            return $"run:delete-failed:{context}";
        }

        if (await documents.GetByKeyAsync(Type, Key) != null)
        {
            return $"run:still-present:{context}";
        }

        return $"run:ok:{context}";
    }
}
