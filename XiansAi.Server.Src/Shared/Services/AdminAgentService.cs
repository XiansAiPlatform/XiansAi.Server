using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace Shared.Services;

public interface IAdminAgentService
{
    Task<ServiceResult<AgentListResult>> GetAgentInstancesAsync(string tenantId, int? page, int? pageSize);
    Task<ServiceResult<Agent>> GetAgentInstanceByIdAsync(string agentId);
    Task<ServiceResult<Agent>> UpdateAgentInstanceAsync(string agentId, UpdateAgentRequest request);
    Task<ServiceResult<bool>> DeleteAgentInstanceAsync(string agentId);
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
    private readonly IAgentDeletionService _agentDeletionService;
    private readonly ILogger<AdminAgentService> _logger;
    private readonly ITenantContext _tenantContext;

    public AdminAgentService(
        IAgentRepository agentRepository,
        IAgentDeletionService agentDeletionService,
        ILogger<AdminAgentService> logger,
        ITenantContext tenantContext
    )
    {
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _agentDeletionService = agentDeletionService ?? throw new ArgumentNullException(nameof(agentDeletionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Gets a paginated list of agent instances for a tenant.
    /// </summary>
    public async Task<ServiceResult<AgentListResult>> GetAgentInstancesAsync(string tenantId, int? page, int? pageSize)
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
    /// Gets an agent instance by its MongoDB ObjectId.
    /// </summary>
    public async Task<ServiceResult<Agent>> GetAgentInstanceByIdAsync(string agentId)
    {
        try
        {
            _logger.LogInformation("Retrieving agent instance by ID {AgentId}", agentId);

            if (string.IsNullOrWhiteSpace(agentId))
            {
                return ServiceResult<Agent>.BadRequest("Agent ID is required");
            }

            var agent = await _agentRepository.GetByIdInternalAsync(agentId);
            if (agent == null)
            {
                _logger.LogWarning("Agent with ID {AgentId} not found", agentId);
                return ServiceResult<Agent>.NotFound($"Agent with ID '{agentId}' not found");
            }

            return ServiceResult<Agent>.Success(agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent instance by ID {AgentId}", agentId);
            return ServiceResult<Agent>.InternalServerError(
                "An error occurred while retrieving the agent instance");
        }
    }

    /// <summary>
    /// Updates an agent instance.
    /// </summary>
    public async Task<ServiceResult<Agent>> UpdateAgentInstanceAsync(string agentId, UpdateAgentRequest request)
    {
        try
        {
            _logger.LogInformation("Updating agent instance {AgentId}", agentId);

            if (string.IsNullOrWhiteSpace(agentId))
            {
                return ServiceResult<Agent>.BadRequest("Agent ID is required");
            }

            var agent = await _agentRepository.GetByIdInternalAsync(agentId);
            if (agent == null)
            {
                _logger.LogWarning("Agent with ID {AgentId} not found", agentId);
                return ServiceResult<Agent>.NotFound($"Agent with ID '{agentId}' not found");
            }

            // Update fields if provided
            if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != agent.Name)
            {
                // Check if new name already exists in the same tenant
                var existingAgent = await _agentRepository.GetByNameInternalAsync(request.Name, agent.Tenant);
                if (existingAgent != null && existingAgent.Id != agentId)
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
                _logger.LogWarning("Failed to update agent {AgentId}", agentId);
                return ServiceResult<Agent>.InternalServerError("Failed to update the agent");
            }

            _logger.LogInformation("Successfully updated agent instance {AgentId}", agentId);
            return ServiceResult<Agent>.Success(agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent instance {AgentId}", agentId);
            return ServiceResult<Agent>.InternalServerError(
                "An error occurred while updating the agent instance");
        }
    }

    /// <summary>
    /// Deletes an agent instance using the AgentDeletionService.
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteAgentInstanceAsync(string agentId)
    {
        try
        {
            _logger.LogInformation("Deleting agent instance {AgentId}", agentId);

            if (string.IsNullOrWhiteSpace(agentId))
            {
                return ServiceResult<bool>.BadRequest("Agent ID is required");
            }

            var agent = await _agentRepository.GetByIdInternalAsync(agentId);
            if (agent == null)
            {
                _logger.LogWarning("Agent with ID {AgentId} not found", agentId);
                return ServiceResult<bool>.NotFound($"Agent with ID '{agentId}' not found");
            }

            // Use AgentDeletionService to delete the agent (handles cascading deletes)
            var deletionResult = await _agentDeletionService.DeleteAgentAsync(agent.Name, agent.SystemScoped);
            if (!deletionResult.IsSuccess)
            {
                _logger.LogWarning("Failed to delete agent {AgentId}: {Error}", agentId, deletionResult.ErrorMessage);
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

            _logger.LogInformation("Successfully deleted agent instance {AgentId}", agentId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent instance {AgentId}", agentId);
            return ServiceResult<bool>.InternalServerError(
                "An error occurred while deleting the agent instance");
        }
    }
}

