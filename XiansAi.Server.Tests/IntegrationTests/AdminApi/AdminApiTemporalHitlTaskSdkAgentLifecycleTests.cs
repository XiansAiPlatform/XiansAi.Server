using Temporalio.Activities;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Tasks.Models;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System HITL agent authored with Xians.Lib. Chat ExecuteAsync a Review workflow that
/// StartTaskAsync, then UpdateDraft/UpdateMetadata/PerformAction from a Temporal activity
/// or from workflow code (Lib stubs signals to TaskActivities), then GetResultAsync.
/// See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/hitl-tasks/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalHitlTaskSdkAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalHitlTaskSdkAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public Task HitlTaskSdkAgent_ProgressOps_FromActivity()
        => RunProgressCycleAsync(fromWorkflow: false);

    [Fact]
    public Task HitlTaskSdkAgent_ProgressOps_FromWorkflow()
        => RunProgressCycleAsync(fromWorkflow: true);

    private async Task RunProgressCycleAsync(bool fromWorkflow)
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"HITLSdk {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var expectedRun = fromWorkflow ? "run:ok:workflow:" : "run:ok:activity:";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var agent = RegisterHitlSdkAgent(host, agentName, fromWorkflow);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(ownerTenant, agentName);
        var activationId = await ActivateLibAgentAsync(ownerTenant, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, expectedRun, userText: "run");

        await RemoveLibActivationAsync(ownerTenant, activationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterHitlSdkAgent(
        LibAgentWorkflowHost host,
        string agentName,
        bool fromWorkflow)
    {
        var agent = host.RegisterTemplate(
            agentName,
            "Progresses a HITL task through TaskCollection from workflow and activity",
            enableTasks: true);
        var review = agent.Workflows.DefineCustom<HitlSdkReviewWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Review");
        review.AddActivity(new HitlSdkActivities());

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context => HandleHitlSdkChatAsync(context, fromWorkflow));
        return agent;
    }

    private static async Task HandleHitlSdkChatAsync(UserMessageContext context, bool fromWorkflow)
    {
        try
        {
            var taskName = Guid.NewGuid().ToString("N")[..12];
            var result = await XiansContext.Workflows.ExecuteAsync<HitlSdkReviewWorkflow, string>(
                new object[] { taskName, fromWorkflow },
                uniqueKey: taskName);
            await context.ReplyAsync(result);
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }
}

[Workflow("placeholder:HitlSdkReview")]
public class HitlSdkReviewWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string taskName, bool fromWorkflow)
    {
        var handle = await XiansContext.CurrentAgent.Tasks.StartTaskAsync(
            new TaskWorkflowRequest
            {
                TaskName = taskName,
                Title = "SDK review",
                Description = $"Review {taskName}",
                DraftWork = $"draft:{taskName}",
                Actions = ["approve", "reject"]
            });

        var taskId = CollectionTaskId(handle.Id);
        if (fromWorkflow)
        {
            await HitlSdkDispatch.ProgressAsync(taskId, taskName);
        }
        else
        {
            await Workflow.ExecuteActivityAsync(
                (HitlSdkActivities activities) => activities.ProgressAsync(taskId, taskName),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
        }

        var result = await XiansContext.CurrentAgent.Tasks.GetResultAsync(handle);
        var context = fromWorkflow ? "workflow" : "activity";
        if (result.TimedOut || !result.Completed)
        {
            return $"run:not-completed:{context}";
        }

        if (!string.Equals(result.InitialWork, $"draft:{taskName}", StringComparison.Ordinal) ||
            !string.Equals(result.FinalWork, $"revised:{taskName}", StringComparison.Ordinal) ||
            !string.Equals(result.PerformedAction, "approve", StringComparison.Ordinal) ||
            !string.Equals(result.Comment, "looks-good", StringComparison.Ordinal))
        {
            return $"run:result-mismatch:{context}";
        }

        if (result.Metadata == null || !result.Metadata.ContainsKey("source"))
        {
            return $"run:no-meta:{context}";
        }

        return $"run:ok:{context}:{result.PerformedAction}";
    }

    private static string CollectionTaskId(string workflowId)
    {
        var colon = workflowId.LastIndexOf(':');
        return colon < 0 ? workflowId : workflowId[(colon + 1)..];
    }
}

public class HitlSdkActivities
{
    [Activity]
    public Task ProgressAsync(string taskId, string taskName)
    {
        return HitlSdkDispatch.ProgressAsync(taskId, taskName);
    }
}

internal static class HitlSdkDispatch
{
    public static async Task ProgressAsync(string taskId, string taskName)
    {
        var tasks = XiansContext.CurrentAgent.Tasks;
        await tasks.UpdateDraftAsync(taskId, $"revised:{taskName}");
        await tasks.UpdateMetadataAsync(taskId, new Dictionary<string, object> { ["source"] = "sdk-hitl" });
        await tasks.PerformActionAsync(taskId, "approve", "looks-good");
    }
}
