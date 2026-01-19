using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace Shared.Services;

public interface IAdminAgentService
{
    Task<ServiceResult<AgentListResult>> GetAgentDeploymentsAsync(string tenantId, int? page, int? pageSize);
    Task<ServiceResult<AgentWithDefinitions>> GetAgentDeploymentByNameAsync(string agentName, string tenantId);
    Task<ServiceResult<Agent>> UpdateAgentDeploymentAsync(string agentName, string tenantId, UpdateAgentRequest request);
    Task<ServiceResult<bool>> DeleteAgentDeploymentAsync(string agentName, string tenantId);
}

public class AgentListResult
{
    public List<Agent> Agents { get; set; } = new();
    public PaginationInfo Pagination { get; set; } = new();
}

public class PaginationInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public bool HasNext { get; set; }
    public bool HasPrevious { get; set; }
}

public class UpdateAgentRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? OnboardingJson { get; set; }
}

/// <summary>
/// Service for managing agent instances in the AdminApi context.
/// </summary>
public class AdminAgentService : IAdminAgentService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IAgentDeletionService _agentDeletionService;
    private readonly ILogger<AdminAgentService> _logger;
    private readonly ITenantContext _tenantContext;

    public AdminAgentService(
        IAgentRepository agentRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        IAgentDeletionService agentDeletionService,
        ILogger<AdminAgentService> logger,
        ITenantContext tenantContext
    )
    {
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
        _agentDeletionService = agentDeletionService ?? throw new ArgumentNullException(nameof(agentDeletionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Gets a paginated list of agent instances for a tenant.
    /// </summary>
    public async Task<ServiceResult<AgentListResult>> GetAgentDeploymentsAsync(string tenantId, int? page, int? pageSize)
    {
        try
        {
            _logger.LogInformation("Retrieving agent instances for tenant {TenantId}", tenantId);

            // Get tenant-scoped agents (instances) - SystemScoped = false
            var agents = await _agentRepository.GetAgentsWithPermissionAsync(
                _tenantContext.LoggedInUser ?? "system", 
                tenantId);
            
            // Filter to only tenant-scoped instances (not system-scoped templates)
            var instances = agents.Where(a => !a.SystemScoped && a.Tenant == tenantId).ToList();

            // Apply pagination
            var pageNum = page ?? 1;
            var pageSizeNum = Math.Min(pageSize ?? 20, 100);
            var skip = (pageNum - 1) * pageSizeNum;
            var total = instances.Count;
            var paginated = instances.Skip(skip).Take(pageSizeNum).ToList();

            var result = new AgentListResult
            {
                Agents = paginated,
                Pagination = new PaginationInfo
                {
                    Page = pageNum,
                    PageSize = pageSizeNum,
                    TotalPages = (int)Math.Ceiling(total / (double)pageSizeNum),
                    TotalItems = total,
                    HasNext = skip + pageSizeNum < total,
                    HasPrevious = pageNum > 1
                }
            };

            _logger.LogInformation("Found {Count} agent instances for tenant {TenantId}", total, tenantId);
            return ServiceResult<AgentListResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent instances for tenant {TenantId}", tenantId);
            return ServiceResult<AgentListResult>.InternalServerError(
                "An error occurred while retrieving agent instances");
        }
    }

    /// <summary>
    /// Gets an agent instance by its name and tenant, including its workflow definitions.
    /// </summary>
    public async Task<ServiceResult<AgentWithDefinitions>> GetAgentDeploymentByNameAsync(string agentName, string tenantId)
    {
        try
        {
            _logger.LogInformation("Retrieving agent instance by name {AgentName} in tenant {TenantId}", agentName, tenantId);

            if (string.IsNullOrWhiteSpace(agentName))
            {
                return ServiceResult<AgentWithDefinitions>.BadRequest("Agent name is required");
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResult<AgentWithDefinitions>.BadRequest("Tenant ID is required");
            }

            var agent = await _agentRepository.GetByNameInternalAsync(agentName, tenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent with name {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<AgentWithDefinitions>.NotFound($"Agent with name '{agentName}' not found in tenant '{tenantId}'");
            }

            // Fetch workflow definitions for this agent
            var definitions = await _flowDefinitionRepository.GetByNameAsync(agentName, tenantId);
            
            _logger.LogInformation("Found {Count} workflow definitions for agent {AgentName}", definitions?.Count ?? 0, agentName);

            var result = new AgentWithDefinitions
            {
                Agent = agent,
                Definitions = definitions ?? new List<FlowDefinition>()
            };

            return ServiceResult<AgentWithDefinitions>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent instance by name {AgentName} in tenant {TenantId}", agentName, tenantId);
            return ServiceResult<AgentWithDefinitions>.InternalServerError(
                "An error occurred while retrieving the agent instance");
        }
    }

    /// <summary>
    /// Updates an agent instance.
    /// </summary>
    public async Task<ServiceResult<Agent>> UpdateAgentDeploymentAsync(string agentName, string tenantId, UpdateAgentRequest request)
    {
        try
        {
            _logger.LogInformation("Updating agent instance {AgentName} in tenant {TenantId}", agentName, tenantId);

            if (string.IsNullOrWhiteSpace(agentName))
            {
                return ServiceResult<Agent>.BadRequest("Agent name is required");
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResult<Agent>.BadRequest("Tenant ID is required");
            }

            var agent = await _agentRepository.GetByNameInternalAsync(agentName, tenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent with name {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<Agent>.NotFound($"Agent with name '{agentName}' not found in tenant '{tenantId}'");
            }

            // Update fields if provided
            if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != agent.Name)
            {
                // Check if new name already exists in the same tenant
                var existingAgent = await _agentRepository.GetByNameInternalAsync(request.Name, agent.Tenant);
                if (existingAgent != null && existingAgent.Id != agent.Id)
                {
                    _logger.LogWarning("Agent with name {Name} already exists in tenant {TenantId}", request.Name, agent.Tenant);
                    return ServiceResult<Agent>.Conflict($"Agent with name '{request.Name}' already exists in tenant");
                }
                agent.Name = request.Name;
            }

            if (request.Description != null)
            {
                agent.Description = request.Description;
            }

            if (request.OnboardingJson != null)
            {
                agent.OnboardingJson = request.OnboardingJson;
            }

            var updated = await _agentRepository.UpdateInternalAsync(agent.Id, agent);
            if (!updated)
            {
                _logger.LogWarning("Failed to update agent {AgentName} in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<Agent>.InternalServerError("Failed to update the agent");
            }

            _logger.LogInformation("Successfully updated agent instance {AgentName} in tenant {TenantId}", agentName, tenantId);
            return ServiceResult<Agent>.Success(agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent instance {AgentName} in tenant {TenantId}", agentName, tenantId);
            return ServiceResult<Agent>.InternalServerError(
                "An error occurred while updating the agent instance");
        }
    }

    /// <summary>
    /// Deletes an agent instance using the AgentDeletionService.
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteAgentDeploymentAsync(string agentName, string tenantId)
    {
        try
        {
            _logger.LogInformation("Deleting agent instance {AgentName} in tenant {TenantId}", agentName, tenantId);

            if (string.IsNullOrWhiteSpace(agentName))
            {
                return ServiceResult<bool>.BadRequest("Agent name is required");
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResult<bool>.BadRequest("Tenant ID is required");
            }

            var agent = await _agentRepository.GetByNameInternalAsync(agentName, tenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent with name {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<bool>.NotFound($"Agent with name '{agentName}' not found in tenant '{tenantId}'");
            }

            // Use AgentDeletionService to delete the agent (handles cascading deletes)
            var deletionResult = await _agentDeletionService.DeleteAgentAsync(agent.Name, agent.SystemScoped);
            if (!deletionResult.IsSuccess)
            {
                _logger.LogWarning("Failed to delete agent {AgentName} in tenant {TenantId}: {Error}", agentName, tenantId, deletionResult.ErrorMessage);
                return deletionResult.StatusCode switch
                {
                    StatusCode.BadRequest => ServiceResult<bool>.BadRequest(deletionResult.ErrorMessage ?? "Bad request"),
                    StatusCode.Forbidden => ServiceResult<bool>.Forbidden(deletionResult.ErrorMessage ?? "Forbidden"),
                    StatusCode.NotFound => ServiceResult<bool>.NotFound(deletionResult.ErrorMessage ?? "Not found"),
                    StatusCode.Conflict => ServiceResult<bool>.Conflict(deletionResult.ErrorMessage ?? "Conflict"),
                    StatusCode.InternalServerError => ServiceResult<bool>.InternalServerError(deletionResult.ErrorMessage ?? "Internal server error"),
                    _ => ServiceResult<bool>.InternalServerError(deletionResult.ErrorMessage ?? "An error occurred")
                };
            }

            _logger.LogInformation("Successfully deleted agent instance {AgentName} in tenant {TenantId}", agentName, tenantId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent instance {AgentName} in tenant {TenantId}", agentName, tenantId);
            return ServiceResult<bool>.InternalServerError(
                "An error occurred while deleting the agent instance");
        }
    }
}

