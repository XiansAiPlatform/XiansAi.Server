using MongoDB.Bson;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;
using Shared.Auth;
using Features.WebApi.Models;
using Shared.Utils.Services;

namespace Features.WebApi.Services;

// Request DTOs
public class CreateTenantRequest 
{
    public required string TenantId { get; set; }
    public required string Name { get; set; }
    public required string Domain { get; set; }
    public string? Description { get; set; }
    public Logo? Logo { get; set; }
    public string? Timezone { get; set; }
}

public class UpdateTenantRequest
{
    public string? Name { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public Logo? Logo { get; set; }
    public string? Timezone { get; set; }
}

public class TenantCreatedResult
{
    public Tenant Tenant { get; set; } = default!;
    public string Location { get; set; } = string.Empty;
}

public interface ITenantService
{
    Task<ServiceResult<Tenant>> GetTenantById(string id);
    Task<ServiceResult<Tenant>> GetTenantByDomain(string domain);
    Task<ServiceResult<Tenant>> GetTenantByTenantId(string tenantId);
    Task<ServiceResult<List<Tenant>>> GetAllTenants();
    Task<ServiceResult<TenantCreatedResult>> CreateTenant(CreateTenantRequest request);
    Task<ServiceResult<Tenant>> UpdateTenant(string id, UpdateTenantRequest request);
    Task<ServiceResult<bool>> DeleteTenant(string id);
    Task<ServiceResult<bool>> AddAgent(string tenantId, Agent agent);
    Task<ServiceResult<bool>> UpdateAgent(string tenantId, string agentName, Agent agent);
    Task<ServiceResult<bool>> RemoveAgent(string tenantId, string agentName);
    Task<ServiceResult<bool>> AddFlowToAgent(string tenantId, string agentName, Flow flow);
}

public class TenantService : ITenantService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<TenantService> _logger;
    private readonly ITenantContext _tenantContext;

    public TenantService(
        ITenantRepository tenantRepository,
        ILogger<TenantService> logger,
        ITenantContext tenantContext)
    {
        _tenantRepository = tenantRepository ?? throw new ArgumentNullException(nameof(tenantRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<ServiceResult<Tenant>> GetTenantById(string id)
    {
        try
        {
            var tenant = await _tenantRepository.GetByIdAsync(id);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with ID {Id}", id);
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> GetTenantByDomain(string domain)
    {
        try
        {
            var tenant = await _tenantRepository.GetByDomainAsync(domain);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with domain {Domain} not found", domain);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with domain {Domain}", domain);
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> GetTenantByTenantId(string tenantId)
    {
        try
        {
            var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", tenantId);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with tenant ID {TenantId}", tenantId);
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<List<Tenant>>> GetAllTenants()
    {
        try
        {
            var tenants = await _tenantRepository.GetAllAsync();
            return ServiceResult<List<Tenant>>.Success(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all tenants");
            return ServiceResult<List<Tenant>>.InternalServerError("An error occurred while retrieving tenants.");
        }
    }

    public async Task<ServiceResult<TenantCreatedResult>> CreateTenant(CreateTenantRequest request)
    {
        try
        {
            var tenant = new Tenant
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = request.TenantId,
                Name = request.Name,
                Domain = request.Domain,
                Description = request.Description,
                Logo = request.Logo,
                Timezone = request.Timezone,
                CreatedBy = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("Logged in user is not set"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _tenantRepository.CreateAsync(tenant);
            _logger.LogInformation("Created new tenant with ID {Id}", tenant.Id);
            
            var result = new TenantCreatedResult
            {
                Tenant = tenant,
                Location = $"/api/tenants/{tenant.Id}"
            };
            return ServiceResult<TenantCreatedResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            return ServiceResult<TenantCreatedResult>.InternalServerError("An error occurred while creating the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> UpdateTenant(string id, UpdateTenantRequest request)
    {
        try
        {
            var existingTenant = await _tenantRepository.GetByIdAsync(id);
            if (existingTenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found for update", id);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            // Update only the properties that are provided in the request
            if (request.Name != null)
                existingTenant.Name = request.Name;
            
            if (request.Domain != null)
                existingTenant.Domain = request.Domain;
            
            if (request.Description != null)
                existingTenant.Description = request.Description;
            
            if (request.Logo != null)
                existingTenant.Logo = request.Logo;
            
            if (request.Timezone != null)
                existingTenant.Timezone = request.Timezone;
            
            existingTenant.UpdatedAt = DateTime.UtcNow;
            
            var success = await _tenantRepository.UpdateAsync(id, existingTenant);
            if (success)
            {
                _logger.LogInformation("Updated tenant with ID {Id}", id);
                return ServiceResult<Tenant>.Success(existingTenant);
            }
            else
            {
                _logger.LogError("Failed to update tenant with ID {Id}", id);
                return ServiceResult<Tenant>.BadRequest("Failed to update tenant.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant with ID {Id}", id);
            return ServiceResult<Tenant>.InternalServerError("An error occurred while updating the tenant.");
        }
    }

    public async Task<ServiceResult<bool>> DeleteTenant(string id)
    {
        try
        {
            var success = await _tenantRepository.DeleteAsync(id);
            if (success)
            {
                _logger.LogInformation("Deleted tenant with ID {Id}", id);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Tenant with ID {Id} not found for deletion", id);
                return ServiceResult<bool>.NotFound("Tenant not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tenant with ID {Id}", id);
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the tenant.");
        }
    }

    public async Task<ServiceResult<bool>> AddAgent(string tenantId, Agent agent)
    {
        try
        {
            agent.CreatedAt = DateTime.UtcNow;
            agent.UpdatedAt = DateTime.UtcNow;
            agent.CreatedBy = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("Logged in user is not set");

            var success = await _tenantRepository.AddAgentAsync(tenantId, agent);
            if (success)
            {
                _logger.LogInformation("Added agent {AgentName} to tenant {TenantId}", agent.Name, tenantId);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Failed to add agent {AgentName} to tenant {TenantId}", agent.Name, tenantId);
                return ServiceResult<bool>.NotFound("Tenant not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding agent {AgentName} to tenant {TenantId}", agent.Name, tenantId);
            return ServiceResult<bool>.InternalServerError("An error occurred while adding the agent.");
        }
    }

    public async Task<ServiceResult<bool>> UpdateAgent(string tenantId, string agentName, Agent agent)
    {
        try
        {
            agent.UpdatedAt = DateTime.UtcNow;
            var success = await _tenantRepository.UpdateAgentAsync(tenantId, agentName, agent);
            if (success)
            {
                _logger.LogInformation("Updated agent {AgentName} in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Agent {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<bool>.NotFound("Agent not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent {AgentName} in tenant {TenantId}", agentName, tenantId);
            return ServiceResult<bool>.InternalServerError("An error occurred while updating the agent.");
        }
    }

    public async Task<ServiceResult<bool>> RemoveAgent(string tenantId, string agentName)
    {
        try
        {
            var success = await _tenantRepository.RemoveAgentAsync(tenantId, agentName);
            if (success)
            {
                _logger.LogInformation("Removed agent {AgentName} from tenant {TenantId}", agentName, tenantId);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Agent {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<bool>.NotFound("Agent not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing agent {AgentName} from tenant {TenantId}", agentName, tenantId);
            return ServiceResult<bool>.InternalServerError("An error occurred while removing the agent.");
        }
    }

    public async Task<ServiceResult<bool>> AddFlowToAgent(string tenantId, string agentName, Flow flow)
    {
        try
        {
            flow.CreatedAt = DateTime.UtcNow;
            flow.UpdatedAt = DateTime.UtcNow;
            flow.CreatedBy = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("Logged in user is not set");

            var success = await _tenantRepository.AddFlowToAgentAsync(tenantId, agentName, flow);
            if (success)
            {
                _logger.LogInformation("Added flow {FlowName} to agent {AgentName} in tenant {TenantId}", flow.Name, agentName, tenantId);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Failed to add flow {FlowName} to agent {AgentName} in tenant {TenantId}", flow.Name, agentName, tenantId);
                return ServiceResult<bool>.NotFound("Agent not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding flow {FlowName} to agent {AgentName} in tenant {TenantId}", flow.Name, agentName, tenantId);
            return ServiceResult<bool>.InternalServerError("An error occurred while adding the flow.");
        }
    }
} 