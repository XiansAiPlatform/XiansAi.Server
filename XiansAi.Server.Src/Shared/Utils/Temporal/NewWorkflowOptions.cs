using Shared.Auth;
using Shared.Utils;
using Temporalio.Client;
using Temporalio.Common;

public class NewWorkflowOptions : WorkflowOptions
{
    public NewWorkflowOptions(string? proposedId, string workFlowType, ITenantContext tenantContext, string? queueName, string? agentName, string? assignment)
    {
        if (string.IsNullOrEmpty(tenantContext.TenantId) || string.IsNullOrEmpty(tenantContext.LoggedInUser))
        {
            throw new InvalidOperationException("TenantId and LoggedInUser are required to create workflow options");
        }

        Id = proposedId ?? GenerateNewWorkflowId(null, agentName ?? workFlowType, workFlowType, tenantContext);
        TaskQueue = GetTemporalQueueName(workFlowType, queueName);
        Memo = GetMemo(workFlowType, tenantContext, assignment, queueName, agentName);
        TypedSearchAttributes = GetSearchAttributes(workFlowType, tenantContext, agentName, assignment);
    }

    private SearchAttributeCollection GetSearchAttributes(string workFlowType, ITenantContext tenantContext, string? agent, string? assignment)
    {
        var searchAttributesBuilder = new SearchAttributeCollection.Builder()
                    .Set(SearchAttributeKey.CreateKeyword(Constants.TenantIdKey), tenantContext.TenantId)
                    .Set(SearchAttributeKey.CreateKeyword(Constants.AgentKey), agent ?? workFlowType)
                    .Set(SearchAttributeKey.CreateKeyword(Constants.UserIdKey), tenantContext.LoggedInUser!);

        if (!string.IsNullOrEmpty(assignment))
        {
            searchAttributesBuilder.Set(SearchAttributeKey.CreateKeyword(Constants.AssignmentKey), assignment);
        }

        return searchAttributesBuilder.ToSearchAttributeCollection();
    }

    private string GetTemporalQueueName(string workFlowType, string? queueName)
    {
        var queueFullName = string.IsNullOrEmpty(queueName) ? workFlowType : queueName + "--" + workFlowType;

        return queueFullName;
    }

    private Dictionary<string, object> GetMemo(string workFlowType, ITenantContext tenantContext, string? assignment, string? queueName, string? agentName)
    {
        var memo = new Dictionary<string, object> {
            { Constants.TenantIdKey, tenantContext.TenantId },
            { Constants.AgentKey, agentName ?? workFlowType },
            { Constants.UserIdKey, tenantContext.LoggedInUser! },
        };
        if (!string.IsNullOrEmpty(assignment))
        {
            memo.Add(Constants.AssignmentKey, assignment);
        }
        if (!string.IsNullOrEmpty(queueName))
        {
            memo.Add(Constants.QueueNameKey, queueName);
        }
        return memo;
    }

    public static string GenerateNewWorkflowId(string? proposedId, string agentName, string workflowType, ITenantContext tenantContext)
    {
        var id = string.IsNullOrEmpty(proposedId) ? $"{agentName}:{workflowType}:{Guid.NewGuid()}" : proposedId;
        var tenantWorkflowId = tenantContext.TenantId + ":" + id.Replace(" ", "");
        return tenantWorkflowId;
    }
}
