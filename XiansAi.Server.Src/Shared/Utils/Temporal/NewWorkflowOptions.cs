using Castle.Components.DictionaryAdapter;
using Shared.Auth;
using Shared.Utils;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Common;

public class NewWorkflowOptions : WorkflowOptions
{

    
    public NewWorkflowOptions(string agentName, bool systemScoped, string workflowType, string? idPostfix, ITenantContext tenantContext, string? userId = null)
    {
        if (string.IsNullOrEmpty(tenantContext.TenantId))
        {
            throw new InvalidOperationException("TenantId is required to create workflow options");
        }

        // Use provided userId or fall back to LoggedInUser from tenant context
        var effectiveUserId = userId ?? tenantContext.LoggedInUser;
        if (string.IsNullOrEmpty(effectiveUserId))
        {
            throw new InvalidOperationException("UserId is required to create workflow options (either provided or from tenant context)");
        }

        // for backward compatibility, if idPostfix starts with tenantid:workflowType, remove it
        if (!string.IsNullOrEmpty(idPostfix) && idPostfix.StartsWith(tenantContext.TenantId + ":"))
        {
            //remove tenantid:workflowType from the beginning of the idPostfix
            idPostfix = idPostfix.Replace(tenantContext.TenantId + ":" + workflowType, "");
            //if remaining is starts with : then remove it
            if (idPostfix.StartsWith(":"))
            {
                idPostfix = idPostfix.Substring(1);
            }
        }

        // WorkflowId is always the tenant id + workflow type
        var workflowId = $"{tenantContext.TenantId}:{workflowType}";

        if (!string.IsNullOrEmpty(idPostfix))
        {
            workflowId += ":" + idPostfix;
        }

        // clean up the id to replace '::' with ':'
        workflowId = workflowId.Replace("::", ":");
        

        Id = workflowId;
        TaskQueue = GetTemporalQueueName(workflowType, systemScoped, tenantContext);
        Memo = GetMemo(tenantContext, agentName, systemScoped, effectiveUserId, idPostfix ?? string.Empty);
        TypedSearchAttributes = GetSearchAttributes(tenantContext, agentName, effectiveUserId, idPostfix ?? string.Empty);
        IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting;
    }

    private SearchAttributeCollection GetSearchAttributes(ITenantContext tenantContext, string agent, string userId, string idPostfix)
    {
        if (string.IsNullOrEmpty(tenantContext.TenantId))
        {
            throw new InvalidOperationException("TenantId is required to create workflow options");
        }

        if (string.IsNullOrEmpty(agent))
        {
            throw new InvalidOperationException("Agent is required to create workflow options");
        }

        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("UserId is required to create workflow options");
        }

        var searchAttributesBuilder = new SearchAttributeCollection.Builder()
                    .Set(SearchAttributeKey.CreateKeyword(Constants.TenantIdKey), tenantContext.TenantId)
                    .Set(SearchAttributeKey.CreateKeyword(Constants.AgentKey), agent)
                    .Set(SearchAttributeKey.CreateKeyword(Constants.UserIdKey), userId)
                    .Set(SearchAttributeKey.CreateKeyword(Constants.IdPostfixKey), idPostfix);

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

    private Dictionary<string, object> GetMemo(ITenantContext tenantContext, string agentName, bool systemScoped, string userId, string idPostfix)
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("UserId is required to create workflow memo");
        }

        var memo = new Dictionary<string, object> {
            { Constants.TenantIdKey, tenantContext.TenantId },
            { Constants.AgentKey, agentName },
            { Constants.UserIdKey, userId },
            { Constants.SystemScopedKey, systemScoped },
            { Constants.IdPostfixKey, idPostfix },
        };

        return memo;
    }
}
