using MongoDB.Bson;
using Shared.Auth;
using Shared.Utils.Services;
using Shared.Data.Models;
using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using Auth0.ManagementApi.Models;
using Microsoft.AspNetCore.Authentication;
using Shared.Repositories;

namespace Features.WebApi.Services;

// Request DTOs
public class CreateTenantRequest
{
    public required string TenantId { get; set; }
    public required string Name { get; set; }
    public required string Domain { get; set; }
    public string? Description { get; set; }
    public Logo? Logo { get; set; }
    public string? Theme { get; set; }
    public string? Timezone { get; set; }
    public bool Enabled { get; set; } = true;
}

public class UpdateTenantRequest
{
    public string? Name { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public Logo? Logo { get; set; }
    public string? Theme { get; set; }
    public string? Timezone { get; set; }
    public bool? Enabled { get; set; }
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

    private string? EnsureTenantAccessOrThrow(string tenantId)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateId(tenantId);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while ensuring tenant access: {Message}", ex.Message);
            throw new Exception($"Validation failed: {ex.Message}");
        }
        // If system admin, return null (indicating unrestricted access)
        if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
        {
            return null;
        }

        // If tenant admin and tenantId matches, return the tenant
        if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) &&
            _tenantContext.TenantId == tenantId)
        {
            return tenantId;
        }

        // Otherwise, forbidden
        _logger.LogWarning("Tenant admin attempted to access tenant {Id} but is restricted to their own tenant {TenantId}", tenantId, _tenantContext.TenantId);
        throw new Exception(("Access denied: insufficient permissions"));
    }

    public async Task<ServiceResult<Tenant>> GetTenantById(string id)
    {
        try
        {
            id = Tenant.SanitizeAndValidateId(id);
            var accessableTenantId = _tenantContext.AuthorizedTenantIds?.FirstOrDefault(t => t == id);
            if (accessableTenantId == null)
            {
                _logger.LogWarning("Unauthorized access attempt to tenant with ID {Id}", id);
                return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
            }

            var tenant = await _tenantRepository.GetByTenantIdAsync(id);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                return ServiceResult<Tenant>.Forbidden("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by ID: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
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
            domain = Tenant.SanitizeAndValidateDomain(domain);

            var tenant = await _tenantRepository.GetByDomainAsync(domain);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with domain {Domain} not found", domain);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by domain: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
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
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);

            var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", tenantId);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by domain: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
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
            var tenantId = EnsureTenantAccessOrThrow(_tenantContext.TenantId);
            var tenants = await _tenantRepository.GetAllAsync(tenantId);
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
                Theme = request.Theme,
                Timezone = request.Timezone,
                Enabled = request.Enabled,
                CreatedBy = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("Logged in user is not set"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var validatedTenant = tenant.SanitizeAndValidate();


            await _tenantRepository.CreateAsync(validatedTenant);
            _logger.LogInformation("Created new tenant with ID {Id}", tenant.Id);

            var result = new TenantCreatedResult
            {
                Tenant = tenant,
                Location = $"/api/tenants/{tenant.Id}"
            };
            return ServiceResult<TenantCreatedResult>.Success(result);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while creating tenant: {Message}", ex.Message);
            return ServiceResult<TenantCreatedResult>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000) // Duplicate key error
        {
            _logger.LogWarning("Duplicate tenant ID or domain: {TenantId}", request.TenantId);
            return ServiceResult<TenantCreatedResult>.BadRequest("A tenant with this ID or domain already exists.");
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
            EnsureTenantAccessOrThrow(id);
            id = Tenant.SanitizeAndValidateId(id);

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

            if (request.Theme != null)
                existingTenant.Theme = request.Theme;

            if (request.Enabled.HasValue)
                existingTenant.Enabled = request.Enabled.Value;

            if (request.Timezone != null)
                existingTenant.Timezone = request.Timezone;

            existingTenant.UpdatedAt = DateTime.UtcNow;
            var validatedTenant = existingTenant.SanitizeAndValidate();

            var success = await _tenantRepository.UpdateAsync(id, validatedTenant);
            if (success)
            {
                _logger.LogInformation("Updated tenant with ID {Id}", id);
                return ServiceResult<Tenant>.Success(validatedTenant);
            }
            else
            {
                _logger.LogError("Failed to update tenant with ID {Id}", id);
                return ServiceResult<Tenant>.BadRequest("Failed to update tenant.");
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while updating tenant: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
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
            id = Tenant.SanitizeAndValidateId(id);

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
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while deleting tenant: {Message}", ex.Message);
            return ServiceResult<bool>.BadRequest($"Validation failed: {ex.Message}");
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
            agent.CreatedBy = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("Logged in user is not set");

            var validatedTenantId = Tenant.SanitizeAndValidateId(tenantId);
            var validatedAgent = agent.SanitizeAndValidate();
            var success = await _tenantRepository.AddAgentAsync(validatedTenantId, validatedAgent);
            if (success)
            {
                _logger.LogInformation("Added agent {AgentName} to tenant {TenantId}", validatedAgent.Name, validatedTenantId);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Failed to add agent {AgentName} to tenant {TenantId}", agent.Name, tenantId);
                return ServiceResult<bool>.NotFound("Tenant not found");
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while adding agent: {Message}", ex.Message);
            return ServiceResult<bool>.BadRequest($"Validation failed: {ex.Message}");
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
            var validatedTenantId = Tenant.SanitizeAndValidateId(tenantId);
            var validatedAgentName = Agent.SanitizeAndValidateName(agentName);
            var validatedAgent = agent.SanitizeAndValidate();
            var success = await _tenantRepository.UpdateAgentAsync(validatedTenantId, validatedAgentName, validatedAgent);
            if (success)
            {
                _logger.LogInformation("Updated agent {AgentName} in tenant {TenantId}", validatedAgentName, validatedTenantId);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Agent {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<bool>.NotFound("Agent not found");
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while updating agent: {Message}", ex.Message);
            return ServiceResult<bool>.BadRequest($"Validation failed: {ex.Message}");
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
        { var validatedTenantId = Tenant.SanitizeAndValidateId(tenantId);
            var validatedAgentName = Agent.SanitizeAndValidateName(agentName);

            var success = await _tenantRepository.RemoveAgentAsync(validatedTenantId, validatedAgentName);
            if (success)
            {
                _logger.LogInformation("Removed agent {AgentName} from tenant {TenantId}", validatedAgentName, validatedTenantId);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Agent {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<bool>.NotFound("Agent not found");
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while removing agent: {Message}", ex.Message);
            return ServiceResult<bool>.BadRequest($"Validation failed: {ex.Message}");
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
            var validatedTenantId = Tenant.SanitizeAndValidateId(tenantId);
            var validatedAgentName = Agent.SanitizeAndValidateName(agentName);
            var validatedFlow = flow.SanitizeAndValidate();

            flow.CreatedAt = DateTime.UtcNow;
            flow.UpdatedAt = DateTime.UtcNow;
            flow.CreatedBy = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("Logged in user is not set");

            var success = await _tenantRepository.AddFlowToAgentAsync(validatedTenantId, validatedAgentName, validatedFlow);
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