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
    [Description("Return the authenticated tenant's identifier, not a tenant profile or all platform tenants. A tenant is a workspace containing agents and their activations. To explore its contents, call list_agents, then list_activations; use an activation target with list_workflows, list_schedules, and list_data_types. These tools do not expose tenant profile attributes. Tool arguments cannot change the authenticated tenant; SysAdmins can select another tenant with the X-Tenant-Id connection header.")]
    public string[] ListTenants() => [tenantContext.TenantId];

    [McpServerTool(Name = "list_agents", ReadOnly = true)]
    [Description("List accessible agent names in the authenticated tenant, including system templates. An agent defines behavior and registered workflow types; it can have multiple configured instances called activations. To explore a returned agent, call list_activations with its exact name.")]
    public async Task<object[]> ListAgents([Description("Exact authenticated tenant identifier returned by list_tenants.")] string tenantId)
    {
        AuthorizeTenant(tenantId);
        var tenantAgents = await agents.GetAgentsWithPermissionAsync(tenantContext.LoggedInUser, tenantId);
        var templates = await agents.GetSystemScopedAgentsWithDefinitionsAsync(basicDataOnly: true);
        var loadedAgents = tenantAgents.Concat(templates.Select(item => item.Agent)).DistinctBy(agent => agent.Name);
        return agentPermissions.GetAgentNamesWithPermission(loadedAgents, PermissionLevel.Read)
            .Select(name => (object)new { AgentName = name }).ToArray();
    }

    [McpServerTool(Name = "list_activations", ReadOnly = true)]
    [Description("List activation names for an accessible agent in the authenticated tenant. An activation is a configured instance of an agent, with its own knowledge overrides, schedules, and saved data. Combine its exact name with tenantId and agentName to form the target for list_workflows, list_schedules, list_data_types, and other schedule/data tools. Activation existence does not guarantee a worker is running.")]
    public async Task<object[]> ListActivations(
        [Description("Exact authenticated tenant identifier returned by list_tenants.")] string tenantId,
        [Description("Exact agent name returned by list_agents.")] string agentName)
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
