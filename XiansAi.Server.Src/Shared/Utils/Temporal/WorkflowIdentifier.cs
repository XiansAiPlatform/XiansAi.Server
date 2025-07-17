using Shared.Auth;

public class WorkflowIdentifier
{
    public string WorkflowId { get; set; }
    public string WorkflowType { get; set; }

    public string AgentName { get; set; }

    public WorkflowIdentifier(string identifier, ITenantContext tenantContext)
    {
        if (identifier.StartsWith(tenantContext.TenantId + ":"))
        {
            // we got workflowId
            WorkflowId = identifier;
            WorkflowType = GetWorkflowType(WorkflowId);
            AgentName = GetAgentName(WorkflowType);
        }
        else
        {
            // we got workflowType
            WorkflowType = identifier;
            AgentName = GetAgentName(WorkflowType);
            WorkflowId = GetWorkflowId(WorkflowType, tenantContext);
        }
    }

    public static string GetWorkflowId(string workflowType, ITenantContext tenantContext)
    {
        return tenantContext.TenantId + ":" + workflowType;
    }

    public static string GetWorkflowType(string workflowId)
    {
        return workflowId.Substring(workflowId.IndexOf(":") + 1);
    }

    public static string GetAgentName(string workflowType)
    {
        return workflowType.Split(":")[0];
    }
}