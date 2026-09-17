using ModelContextProtocol;

namespace Features.Mcp.Tools;

public sealed record McpTarget(string TenantId, string AgentName, string ActivationName)
{
    public static void Validate(McpTarget target)
    {
        if (target is null || string.IsNullOrWhiteSpace(target.TenantId) ||
            string.IsNullOrWhiteSpace(target.AgentName) || string.IsNullOrWhiteSpace(target.ActivationName))
            throw new McpException("Target requires tenantId, agentName, and activationName.");
    }
}
