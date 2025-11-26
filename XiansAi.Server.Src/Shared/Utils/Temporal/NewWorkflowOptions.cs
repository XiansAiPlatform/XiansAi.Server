using Shared.Auth;
using Shared.Utils;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Common;

public class NewWorkflowOptions : WorkflowOptions
{
    public NewWorkflowOptions(string agentName, bool systemScoped, string workFlowType, string? proposedId, ITenantContext tenantContext)
    {
        if (string.IsNullOrEmpty(tenantContext.TenantId) || string.IsNullOrEmpty(tenantContext.LoggedInUser))
        {
            throw new InvalidOperationException("TenantId and LoggedInUser are required to create workflow options");
        }

        if (string.IsNullOrEmpty(proposedId))
        {
            proposedId = GenerateNewWorkflowId(workFlowType, tenantContext);
        }
        else
        {
            if (!proposedId.StartsWith(tenantContext.TenantId + ":"))
            {
                proposedId = tenantContext.TenantId + ":" + proposedId;
            }

        }

        Id = proposedId;
        TaskQueue = GetTemporalQueueName(workFlowType, systemScoped, tenantContext);
        Memo = GetMemo(tenantContext, agentName, systemScoped);
        TypedSearchAttributes = GetSearchAttributes(tenantContext, agentName);
        IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting;
    }

    private SearchAttributeCollection GetSearchAttributes(ITenantContext tenantContext, string agent)
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

        return searchAttributesBuilder.ToSearchAttributeCollection();
    }

    private string GetTemporalQueueName(string workFlowType, bool systemScoped, ITenantContext tenantContext)
    {
        // System agents use the same queue as the agent type without the tenant id prefix
        if (systemScoped)
        {
            return workFlowType;
        }
        return tenantContext.TenantId + ":" + workFlowType;
    }

    private Dictionary<string, object> GetMemo(ITenantContext tenantContext, string agentName, bool systemScoped, Dictionary<string, object>? additionalMemo = null)
    {
        var memo = new Dictionary<string, object> {
            { Constants.TenantIdKey, tenantContext.TenantId },
            { Constants.AgentKey, agentName },
            { Constants.UserIdKey, tenantContext.LoggedInUser! },
            { Constants.SystemScopedKey, systemScoped },
        };
        
        // Merge any additional memo entries (e.g., trace context)
        if (additionalMemo != null)
        {
            foreach (var kvp in additionalMemo)
            {
                memo[kvp.Key] = kvp.Value;
            }
        }

        return memo;
    }
    
    /// <summary>
    /// Adds additional entries to the memo dictionary.
    /// This allows adding trace context or other metadata after the options are created.
    /// </summary>
    public void AddToMemo(string key, object value)
    {
        if (Memo is Dictionary<string, object> mutableMemo)
        {
            mutableMemo[key] = value;
        }
        else
        {
            // If memo is read-only, create a new dictionary with the additional entry
            var newMemo = new Dictionary<string, object>(Memo);
            newMemo[key] = value;
            Memo = newMemo;
        }
    }

    public static string GenerateNewWorkflowId(string workflowType, ITenantContext tenantContext)
    {
        var id = $"{workflowType}:{Guid.NewGuid()}";
        var tenantWorkflowId = tenantContext.TenantId + ":" + id;
        return tenantWorkflowId;
    }
}
