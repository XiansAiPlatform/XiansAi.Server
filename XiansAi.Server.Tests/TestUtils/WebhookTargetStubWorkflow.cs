using Shared.Models;
using Temporalio.Workflows;

namespace Tests.TestUtils;

/// <summary>
/// A fake agent workflow with update methods, used to test that update method names are read
/// from a running workflow.
/// </summary>
[Workflow(Dynamic = true)]
public class WebhookTargetStubWorkflow
{
    [WorkflowRun]
    public Task RunAsync(Temporalio.Converters.IRawValue[] args) => Workflow.WaitConditionAsync(() => false);

    [WorkflowUpdate("health-ping")]
    public Task<WebhookResponse> HealthPingAsync(IDictionary<string, string> queryParams, string body) =>
        Task.FromResult(new WebhookResponse(System.Net.HttpStatusCode.OK, "{\"status\":\"ok\"}"));

    [WorkflowUpdate("github-push")]
    public Task<WebhookResponse> GitHubPushAsync(IDictionary<string, string> queryParams, string body) =>
        Task.FromResult(new WebhookResponse(System.Net.HttpStatusCode.OK, "{\"status\":\"received\"}"));
}
