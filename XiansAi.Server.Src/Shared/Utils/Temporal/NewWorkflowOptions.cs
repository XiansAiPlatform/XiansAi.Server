using Shared.Auth;
using Shared.Utils;
using Temporalio.Client;
using Temporalio.Common;

public class NewWorkflowOptions : WorkflowOptions
{
    public NewWorkflowOptions(string? proposedId, string workFlowType, ITenantContext tenantContext, string? queueName, string agentName, string? assignment)
    {
        if (string.IsNullOrEmpty(tenantContext.TenantId) || string.IsNullOrEmpty(tenantContext.LoggedInUser))
        {
            throw new InvalidOperationException("TenantId and LoggedInUser are required to create workflow options");
        }

        if (string.IsNullOrEmpty(proposedId))
        {
            proposedId = GenerateNewWorkflowId(agentName, workFlowType, tenantContext);
        }
        else
        {
            if (!proposedId.StartsWith(tenantContext.TenantId + ":"))
            {
                proposedId = tenantContext.TenantId + ":" + proposedId;
            }

        }

        Id = proposedId;
        TaskQueue = GetTemporalQueueName(workFlowType, queueName);
        Memo = GetMemo(tenantContext, assignment, queueName, agentName);
        TypedSearchAttributes = GetSearchAttributes(tenantContext, agentName, assignment);
    }

    private SearchAttributeCollection GetSearchAttributes(ITenantContext tenantContext, string agent, string? assignment)
    {
        if (string.IsNullOrEmpty(tenantContext.TenantId) || string.IsNullOrEmpty(tenantContext.LoggedInUser))
        {
            throw new InvalidOperationException("TenantId and LoggedInUser are required to create workflow options");
        }

        if (string.IsNullOrEmpty(agent))
        {
            throw new InvalidOperationException("Agent is required to create workflow options");
        }

        var searchAttributesBuilder = new SearchAttributeCollection.Builder()
                    .Set(SearchAttributeKey.CreateKeyword(Constants.TenantIdKey), tenantContext.TenantId)
                    .Set(SearchAttributeKey.CreateKeyword(Constants.AgentKey), agent)
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

        return workFlowType.ToLower().Replace(" ", "").Replace("-", "").Trim();;
    }

    private Dictionary<string, object> GetMemo(ITenantContext tenantContext, string? assignment, string? queueName, string agentName)
    {
        var memo = new Dictionary<string, object> {
            { Constants.TenantIdKey, tenantContext.TenantId },
            { Constants.AgentKey, agentName },
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

    public static string GenerateNewWorkflowId(string agentName, string workflowType, ITenantContext tenantContext)
    {
        var id = $"{agentName}:{workflowType}:{Guid.NewGuid()}";
        var tenantWorkflowId = tenantContext.TenantId + ":" + id.Replace(" ", "");
        return tenantWorkflowId;
    }
}
