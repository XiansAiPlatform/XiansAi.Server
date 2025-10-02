using Shared.Auth;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Features.WebApi.Services;

public interface ITemplateService
{
    Task<ServiceResult<List<AgentWithDefinitions>>> GetSystemScopedAgentDefinitions(bool basicDataOnly = false);
}

/// <summary>
/// Service for managing template agent definitions with system_scoped attribute.
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<TemplateService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateService"/> class.
    /// </summary>
    /// <param name="agentRepository">Repository for agent operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    public TemplateService(
        IAgentRepository agentRepository,
        ILogger<TemplateService> logger,
        ITenantContext tenantContext
    )
    {
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Retrieves all agent definitions that have system_scoped attribute set to true.
    /// System-scoped agents are available to any logged-in user with a valid tenant without permission checks.
    /// </summary>
    /// <param name="basicDataOnly">Whether to return basic data only.</param>
    /// <returns>A service result containing the list of system-scoped agent definitions.</returns>
    public async Task<ServiceResult<List<AgentWithDefinitions>>> GetSystemScopedAgentDefinitions(bool basicDataOnly = false)
    {
        try
        {
            _logger.LogInformation("Retrieving system-scoped agent definitions for user {UserId} in tenant {TenantId} (no permission checks)", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);

            // Get system-scoped agents without permission checks
            // System-scoped agents are available to any logged-in user with a valid tenant
            var systemScopedAgents = await _agentRepository.GetSystemScopedAgentsWithDefinitionsAsync(basicDataOnly);

            _logger.LogInformation("Found {Count} system-scoped agents for user {UserId}", 
                systemScopedAgents.Count, _tenantContext.LoggedInUser);

            return ServiceResult<List<AgentWithDefinitions>>.Success(systemScopedAgents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving system-scoped agent definitions for user {UserId} in tenant {TenantId}", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);
            return ServiceResult<List<AgentWithDefinitions>>.InternalServerError(
                "An error occurred while retrieving system-scoped agent definitions. Error: " + ex.Message);
        }
    }
}
