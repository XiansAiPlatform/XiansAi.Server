using ModelContextProtocol;
using System.ComponentModel;

namespace Features.Mcp.Tools;

[Description("Select one configured agent activation in the authenticated tenant. Discover identifiers using list_tenants, list_agents, and list_activations; reuse exact returned values.")]
public sealed record McpTarget(
    [property: Description("Workspace identifier returned by list_tenants; must match the authenticated tenant.")] string TenantId,
    [property: Description("Agent definition name returned by list_agents; an agent can have multiple activations.")] string AgentName,
    [property: Description("Configured agent instance name returned by list_activations; scopes schedules and saved data.")] string ActivationName)
{
    public static void Validate(McpTarget target)
    {
        if (target is null || string.IsNullOrWhiteSpace(target.TenantId) ||
            string.IsNullOrWhiteSpace(target.AgentName) || string.IsNullOrWhiteSpace(target.ActivationName))
            throw new McpException("Target requires tenantId, agentName, and activationName.");
    }
}
