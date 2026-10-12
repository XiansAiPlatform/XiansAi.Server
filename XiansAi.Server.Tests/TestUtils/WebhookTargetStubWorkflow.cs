using Shared.Models;
using Temporalio.Workflows;

namespace Tests.TestUtils;

/// <summary>
/// Dynamic stand-in for an agent workflow that handles inbound webhooks (Temporal updates), so tests
/// can check that the webhook-eval target discovery reads update method names from a live workflow.
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
