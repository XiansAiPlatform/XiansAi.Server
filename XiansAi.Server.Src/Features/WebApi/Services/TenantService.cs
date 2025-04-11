using MongoDB.Bson;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;
using Shared.Auth;

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

public class TenantService
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

    public async Task<IResult> GetTenantById(string id)
    {
        try
        {
            var tenant = await _tenantRepository.GetByIdAsync(id);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                return Results.NotFound();
            }

            return Results.Ok(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with ID {Id}", id);
            return Results.Problem("An error occurred while retrieving the tenant.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> GetTenantByDomain(string domain)
    {
        try
        {
            var tenant = await _tenantRepository.GetByDomainAsync(domain);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with domain {Domain} not found", domain);
                return Results.NotFound();
            }

            return Results.Ok(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with domain {Domain}", domain);
            return Results.Problem("An error occurred while retrieving the tenant.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> GetTenantByTenantId(string tenantId)
    {
        try
        {
            var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", tenantId);
                return Results.NotFound();
            }

            return Results.Ok(tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with tenant ID {TenantId}", tenantId);
            return Results.Problem("An error occurred while retrieving the tenant.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> GetAllTenants()
    {
        try
        {
            var tenants = await _tenantRepository.GetAllAsync();
            return Results.Ok(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all tenants");
            return Results.Problem("An error occurred while retrieving tenants.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> CreateTenant(CreateTenantRequest request)
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
            return Results.Created($"/api/tenants/{tenant.Id}", tenant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            return Results.Problem("An error occurred while creating the tenant.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> UpdateTenant(string id, UpdateTenantRequest request)
    {
        try
        {
            var existingTenant = await _tenantRepository.GetByIdAsync(id);
            if (existingTenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found for update", id);
                return Results.NotFound();
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
                return Results.Ok(existingTenant);
            }
            else
            {
                _logger.LogError("Failed to update tenant with ID {Id}", id);
                return Results.Problem("Failed to update tenant.", statusCode: StatusCodes.Status500InternalServerError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant with ID {Id}", id);
            return Results.Problem("An error occurred while updating the tenant.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> DeleteTenant(string id)
    {
        try
        {
            var success = await _tenantRepository.DeleteAsync(id);
            if (success)
            {
                _logger.LogInformation("Deleted tenant with ID {Id}", id);
                return Results.Ok();
            }
            else
            {
                _logger.LogWarning("Tenant with ID {Id} not found for deletion", id);
                return Results.NotFound();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tenant with ID {Id}", id);
            return Results.Problem("An error occurred while deleting the tenant.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> AddAgent(string tenantId, Agent agent)
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
                return Results.Ok(agent);
            }
            else
            {
                _logger.LogWarning("Failed to add agent {AgentName} to tenant {TenantId}", agent.Name, tenantId);
                return Results.NotFound();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding agent {AgentName} to tenant {TenantId}", agent.Name, tenantId);
            return Results.Problem("An error occurred while adding the agent.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> UpdateAgent(string tenantId, string agentName, Agent agent)
    {
        try
        {
            agent.UpdatedAt = DateTime.UtcNow;
            var success = await _tenantRepository.UpdateAgentAsync(tenantId, agentName, agent);
            if (success)
            {
                _logger.LogInformation("Updated agent {AgentName} in tenant {TenantId}", agentName, tenantId);
                return Results.Ok(agent);
            }
            else
            {
                _logger.LogWarning("Agent {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return Results.NotFound();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent {AgentName} in tenant {TenantId}", agentName, tenantId);
            return Results.Problem("An error occurred while updating the agent.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> RemoveAgent(string tenantId, string agentName)
    {
        try
        {
            var success = await _tenantRepository.RemoveAgentAsync(tenantId, agentName);
            if (success)
            {
                _logger.LogInformation("Removed agent {AgentName} from tenant {TenantId}", agentName, tenantId);
                return Results.Ok();
            }
            else
            {
                _logger.LogWarning("Agent {AgentName} not found in tenant {TenantId}", agentName, tenantId);
                return Results.NotFound();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing agent {AgentName} from tenant {TenantId}", agentName, tenantId);
            return Results.Problem("An error occurred while removing the agent.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> AddFlowToAgent(string tenantId, string agentName, Flow flow)
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
                return Results.Ok(flow);
            }
            else
            {
                _logger.LogWarning("Failed to add flow {FlowName} to agent {AgentName} in tenant {TenantId}", flow.Name, agentName, tenantId);
                return Results.NotFound();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding flow {FlowName} to agent {AgentName} in tenant {TenantId}", flow.Name, agentName, tenantId);
            return Results.Problem("An error occurred while adding the flow.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
} 