using Temporalio.Client;
using Temporalio.Exceptions;
using Shared.Auth;
using Shared.Models;
using Features.WebApi.Services;

namespace Shared.Utils.Temporal;

public class UpdateService
{
    /// <summary>
    /// Sends a webhook update to a temporal workflow using update-with-start pattern.
    /// Implements manual timeout cancellation to prevent indefinite hanging when no workers are available.
    /// </summary>
    /// <param name="workflowIdentifier">The workflow identifier (can be workflowId or workflowType)</param>
    /// <param name="methodName">The temporal update method name to call</param>
    /// <param name="queryParams">Query parameters from the webhook request</param>
    /// <param name="body">Request body from the webhook</param>
    /// <param name="tenantContext">Tenant context for workflow identification</param>
    /// <param name="temporalClientFactory">Factory to get temporal client</param>
    /// <param name="agentService">Agent service for checking if an agent is system scoped</param>
    /// <param name="timeout">Timeout for the update operation (default: 30 seconds). Manually enforced via cancellation token.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>WebhookResponse from the workflow update method</returns>
    /// <exception cref="WorkflowUpdateRpcTimeoutOrCanceledException">Thrown when the operation times out or is cancelled</exception>
    public static async Task<WebhookResponse> SendWebhookUpdate(
        string workflowIdentifier,
        string methodName,
        IDictionary<string, string> queryParams,
        string body,
        ITenantContext tenantContext,
        ITemporalClientFactory temporalClientFactory,
        IAgentService agentService,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // Parse the workflow identifier to get workflowId and workflowType
        var workflowInfo = new WorkflowIdentifier(workflowIdentifier, tenantContext);
        
        // Get the temporal client
        var client = await temporalClientFactory.GetClientAsync();

        var systemScoped = agentService.IsSystemAgent(workflowInfo.AgentName).Result.Data;
        
        // Create workflow options for update-with-start
        var workflowOptions = new NewWorkflowOptions(
            workflowInfo.AgentName,
            systemScoped,
            workflowInfo.WorkflowType,
            workflowInfo.WorkflowId,
            tenantContext);
        
        // Create the start operation
        var startOperation = WithStartWorkflowOperation.Create(
            workflowInfo.WorkflowType,
            Array.Empty<object?>(),
            workflowOptions);

        // Create a manual timeout cancellation token source
        var timeoutDuration = timeout ?? TimeSpan.FromSeconds(30);
        using var timeoutCts = new CancellationTokenSource(timeoutDuration);
        
        // Combine the provided cancellation token with our timeout token
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);
        
        var rpc = new RpcOptions
        {
            Retry = false,
            CancellationToken = combinedCts.Token
        };
        
        var updateOptions = new WorkflowUpdateWithStartOptions
        {
            StartWorkflowOperation = startOperation,
            Rpc = rpc
        };
        
        // Send the update with start - this will be cancelled by our manual timeout
        var result = await client.ExecuteUpdateWithStartWorkflowAsync<WebhookResponse>(
            methodName,
            [queryParams, body],
            updateOptions);
        
        return result ?? throw new Exception("Webhook update returned null");
    }
}
