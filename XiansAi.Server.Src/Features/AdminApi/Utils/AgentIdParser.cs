namespace Features.AdminApi.Utils;

/// <summary>
/// Utility class for parsing agent IDs in the format {tenantName}@{agentName}
/// </summary>
public static class AgentIdParser
{
    /// <summary>
    /// Parses an agent ID and extracts tenant name and agent name.
    /// Supports both formats: "tenantname@agentname" and just "agentname"
    /// </summary>
    /// <param name="agentId">Agent ID in format {tenantName}@{agentName} or just {agentName}</param>
    /// <param name="defaultTenant">Default tenant to use if agentId doesn't contain @</param>
    /// <returns>Tuple of (tenantName, agentName)</returns>
    public static (string tenant, string agentName) Parse(string agentId, string? defaultTenant = null)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
        }

        var parts = agentId.Split('@', 2);
        
        if (parts.Length == 2)
        {
            // Format: tenantname@agentname
            return (parts[0].Trim(), parts[1].Trim());
        }
        else if (parts.Length == 1)
        {
            // Format: just agentname (use default tenant)
            if (string.IsNullOrWhiteSpace(defaultTenant))
            {
                throw new ArgumentException($"Agent ID '{agentId}' does not contain tenant. Provide defaultTenant or use format 'tenant@agent'.", nameof(agentId));
            }
            return (defaultTenant.Trim(), parts[0].Trim());
        }
        else
        {
            throw new ArgumentException($"Invalid agent ID format: '{agentId}'. Expected format: 'tenant@agent' or 'agent'", nameof(agentId));
        }
    }

    /// <summary>
    /// Formats tenant name and agent name into an agent ID.
    /// </summary>
    /// <param name="tenant">Tenant name</param>
    /// <param name="agentName">Agent name</param>
    /// <returns>Agent ID in format {tenantName}@{agentName}</returns>
    public static string Format(string tenant, string agentName)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentException("Tenant cannot be null or empty", nameof(tenant));
        }
        if (string.IsNullOrWhiteSpace(agentName))
        {
            throw new ArgumentException("Agent name cannot be null or empty", nameof(agentName));
        }

        return $"{tenant}@{agentName}";
    }
}



