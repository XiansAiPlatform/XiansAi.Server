using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Tasks.Models;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// UserMessageContext.GetLastTaskIdAsync reads the last conversation TaskId for the
/// current participant. CreateAndWait / HitlTask stay on the HITL conversation cycle.
/// See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/hitl-tasks/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalHitlLastTaskIdAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalHitlLastTaskIdAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task HitlLastTaskIdAgent_StampAndGetLastTaskId_IsolatesParticipant()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"HITLLast {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var taskName = $"ask-{Guid.NewGuid():N}"[..12];
        var taskId = $"{tenantId}:{agentName}:Task Workflow:{activationName}--{taskName}";
        var owner = $"owner-{Guid.NewGuid():N}@example.com";
        var other = $"other-{Guid.NewGuid():N}@example.com";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterLastTaskAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"stamped:{taskName}",
            userText: $"ask {taskName}",
            participantId: owner);
        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"last:{taskId}",
            userText: "last",
            participantId: owner);
        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "last:none",
            userText: "last",
            participantId: other);

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterLastTaskAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(
            agentName,
            "Stamps a HITL task id on chat and reads it with GetLastTaskIdAsync",
            enableTasks: true);
        agent.Workflows.DefineCustom<HitlLastTaskAskWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Ask");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleLastTaskChatAsync);
        return agent;
    }

    private static async Task HandleLastTaskChatAsync(UserMessageContext context)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var space = text.IndexOf(' ');
            var command = space < 0 ? text : text[..space];
            var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

            if (command == "ask")
            {
                await XiansContext.Workflows.ExecuteAsync<HitlLastTaskAskWorkflow, string>(
                    new object[] { argument }, uniqueKey: argument);
                var taskId =
                    $"{XiansContext.TenantId}:{XiansContext.CurrentAgent.Name}:Task Workflow:{XiansContext.SafeIdPostfix}--{argument}";
                await XiansContext.Messaging.SendChatAsSupervisorAsync(
                    $"need:{argument}",
                    taskId: taskId,
                    participantId: context.Message.ParticipantId);
                await context.ReplyAsync($"stamped:{argument}");
                return;
            }

            if (command == "last")
            {
                var last = await context.GetLastTaskIdAsync();
                await context.ReplyAsync(string.IsNullOrEmpty(last) ? "last:none" : $"last:{last}");
                return;
            }

            await context.ReplyAsync("unknown");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }
}

[Workflow("placeholder:HitlLastAsk")]
public class HitlLastTaskAskWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string taskName)
    {
        await XiansContext.CurrentAgent.Tasks.CreateAsync(
            new TaskWorkflowRequest
            {
                TaskName = taskName,
                Title = taskName,
                Description = $"Ask {taskName}",
                DraftWork = $"draft:{taskName}",
                Actions = ["approve", "reject"],
                SurviveParentClose = true
            });
        return "asked";
    }
}
