using Shared.Auth;
using Shared.Exceptions;

public class WorkflowIdentifier
{

    // EXAMPLE: default:My Agent v1.3.1:Router Bot:ebbb57bd-8428-458f-9618-d8fe3bef103c
    // WORKFLOW ID FORMAT: tenant:Agent Name:Flow Name:IdPostfix
    // WORKFLOW TYPE FORMAT: Agent Name:Flow Name
    public string WorkflowId { get; set; }
    public string WorkflowType { get; set; }

    public string AgentName { get; set; }

    public WorkflowIdentifier(string? identifier, ITenantContext tenantContext)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new InvalidWorkflowIdentifierException(
                identifier,
                "A workflow identifier is required, but none was supplied. " +
                DescribeAcceptedFormats(tenantContext.TenantId));
        }

        // if identifier has 2 ":" then we got workflowId
        if (identifier.Count(c => c == ':') >= 2)
        {
            if (!identifier.StartsWith(tenantContext.TenantId + ":"))
            {
                throw new InvalidWorkflowIdentifierException(
                    identifier,
                    BuildTenantPrefixMismatchMessage(identifier, tenantContext.TenantId));
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

    /// <summary>
    /// Builds the message for the most common caller mistake: sending a workflow id whose
    /// first segment is the agent name rather than the tenant id. Because an identifier with
    /// two or more `:` is always read as a workflow id, the first segment is what we compare
    /// against the tenant, and the caller needs to be told exactly that.
    /// </summary>
    private static string BuildTenantPrefixMismatchMessage(string identifier, string tenantId)
    {
        var segments = identifier.Split(':');
        var firstSegment = segments[0];

        var message =
            $"Invalid workflow identifier `{identifier}`. " +
            $"An identifier containing two or more `:` is read as a full workflow id, so its first segment " +
            $"(`{firstSegment}`) is expected to be the tenant id of the authenticated caller (`{tenantId}`). " +
            DescribeAcceptedFormats(tenantId);

        // Three segments are genuinely ambiguous: the caller either sent
        // `Agent:Flow:Run Suffix` and forgot the tenant prefix, or sent
        // `Wrong Tenant:Agent:Flow`. Spell out both readings rather than guessing one.
        if (segments.Length == 3)
        {
            message +=
                $" If `{firstSegment}` is an agent name and the tenant prefix was left off, send " +
                $"`{tenantId}:{identifier}`, or `{segments[0]}:{segments[1]}` to address the workflow " +
                $"without a run suffix. If `{firstSegment}` was meant as a tenant id, it is not the " +
                $"tenant you are authenticated as.";
        }

        return message;
    }

    private static string DescribeAcceptedFormats(string tenantId)
    {
        return $"Accepted formats are `Agent Name:Flow Name` (a workflow type, which is resolved " +
               $"against your tenant automatically) or `{tenantId}:Agent Name:Flow Name[:Run Suffix]` " +
               $"(a fully qualified workflow id).";
    }

    public static string GetWorkflowId(string workflowType, ITenantContext tenantContext)
    {
        return tenantContext.TenantId + ":" + workflowType;
    }

    public static string BuildWorkflowId(string tenantId, string agentName, string workflowName, string? idPostfix = null)
    {
        var workflowId = $"{tenantId}:{agentName}:{workflowName}";
        if (!string.IsNullOrEmpty(idPostfix))
        {
            workflowId += $":{idPostfix}";
        }
        return workflowId;
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
            throw new InvalidWorkflowIdentifierException(
                workflow,
                $"Cannot determine the workflow type from `{workflow}` because it contains no `:`. " +
                "Expected either `Agent Name:Flow Name` or `tenant:Agent Name:Flow Name[:Run Suffix]`.");
        }
    }

    public static string GetAgentName(string workflowType)
    {
        return workflowType.Split(":")[0].Trim();
    }

    /// <summary>
    /// Extracts the optional id postfix (activation name / run suffix) from a fully qualified
    /// workflow id of the form <c>tenant:Agent:Flow[:Postfix]</c>.
    /// Returns <c>null</c> when the id has fewer than four segments (no postfix).
    /// Multi-segment postfixes are rejoined with <c>:</c>.
    /// </summary>
    public static string? GetIdPostfix(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return null;
        }

        var parts = workflowId.Split(':');
        // tenant:Agent:Flow requires 3 segments; postfix starts at index 3
        if (parts.Length < 4)
        {
            return null;
        }

        var postfix = string.Join(':', parts.Skip(3)).Trim();
        return string.IsNullOrEmpty(postfix) ? null : postfix;
    }
}