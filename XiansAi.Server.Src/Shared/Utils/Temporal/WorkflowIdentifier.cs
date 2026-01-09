using Shared.Auth;

public class WorkflowIdentifier
{

    // EXAMPLE: default:My Agent v1.3.1:Router Bot:ebbb57bd-8428-458f-9618-d8fe3bef103c
    // WORKFLOW ID FORMAT: tenant:Agent Name:Flow Name:IdPostfix
    // WORKFLOW TYPE FORMAT: Agent Name:Flow Name
    public string WorkflowId { get; set; }
    public string WorkflowType { get; set; }

    public string AgentName { get; set; }

    public WorkflowIdentifier(string identifier, ITenantContext tenantContext)
    {
        // if identifier has 2 ":" then we got workflowId
        if (identifier.Count(c => c == ':') >= 2)
        {
            if (!identifier.StartsWith(tenantContext.TenantId + ":"))
            {
                throw new Exception($"Invalid workflow identifier `{identifier}`. Expected to start with tenant id `{tenantContext.TenantId}`");
            }
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

    public static string GetWorkflowType(string workflow)
    {
        // if workflow has 1 ":" then we got workflowType
        if (workflow.Count(c => c == ':') == 1) {
            return workflow;
        } 
        else if (workflow.Count(c => c == ':') >= 2) // We got workflowId
        {
            var parts = workflow.Split(":");
            return parts[1] + ":" + parts[2];
        }
        else {
            throw new Exception($"Invalid workflow identifier `{workflow}`. Expected to have 1 or 2 `:`");
        }
    }

    public static string GetAgentName(string workflowType)
    {
        return workflowType.Split(":")[0].Trim();
    }
}