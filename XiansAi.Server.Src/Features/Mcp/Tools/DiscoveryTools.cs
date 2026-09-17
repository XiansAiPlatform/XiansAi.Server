using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;

namespace Features.Mcp.Tools;

[McpServerToolType]
public sealed class DiscoveryTools(ITenantContext tenantContext, IAgentRepository agents,
    IActivationRepository activations, IPermissionsService permissions, IAgentPermissionRepository agentPermissions)
{
    private void AuthorizeTenant(string tenantId)
    {
        if (tenantId != tenantContext.TenantId) throw new McpException("Tenant access denied.");
    }

    [McpServerTool(Name = "list_tenants", ReadOnly = true)]
    [Description("List the tenant selected by this authenticated connection. A tool argument cannot change authenticated tenant context; SysAdmins can select another tenant with the X-Tenant-Id connection header.")]
    public string[] ListTenants() => [tenantContext.TenantId];

    [McpServerTool(Name = "list_agents", ReadOnly = true)]
    [Description("Discover accessible agents in the authenticated tenant, including system templates.")]
    public async Task<object[]> ListAgents(string tenantId)
    {
        AuthorizeTenant(tenantId);
        var tenantAgents = await agents.GetAgentsWithPermissionAsync(tenantContext.LoggedInUser, tenantId);
        var templates = await agents.GetSystemScopedAgentsWithDefinitionsAsync(basicDataOnly: true);
        var loadedAgents = tenantAgents.Concat(templates.Select(item => item.Agent)).DistinctBy(agent => agent.Name);
        return agentPermissions.GetAgentNamesWithPermission(loadedAgents, PermissionLevel.Read)
            .Select(name => (object)new { AgentName = name }).ToArray();
    }

    [McpServerTool(Name = "list_activations", ReadOnly = true)]
    [Description("Discover activations of an accessible agent in the authenticated tenant.")]
    public async Task<object[]> ListActivations(string tenantId, string agentName)
    {
        AuthorizeTenant(tenantId);
        var access = await permissions.HasReadPermission(agentName);
        if (!access.IsSuccess || !access.Data) throw new McpException("Agent access denied.");
        var agent = await agents.GetByNameAsync(agentName, tenantId, tenantContext.LoggedInUser, tenantContext.UserRoles);
        if (agent is null) throw new McpException("Agent not found.");
        return (await activations.GetByAgentNameAsync(agentName, tenantId))
            .Select(activation => (object)new { ActivationName = activation.Name }).ToArray();
    }
}
