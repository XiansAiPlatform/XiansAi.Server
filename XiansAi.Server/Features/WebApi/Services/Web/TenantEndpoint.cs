using MongoDB.Bson;
using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.Database.Models;
using XiansAi.Server.Database.Repositories;

namespace Features.WebApi.Services.Web;

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

public class TenantEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<TenantEndpoint> _logger;

    public TenantEndpoint(
        IDatabaseService databaseService,
        ILogger<TenantEndpoint> logger
    )
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task<IResult> GetTenants()
    {
        var repository = new TenantRepository(await _databaseService.GetDatabase());
        var tenants = await repository.GetAllAsync();
        _logger.LogInformation("Retrieved {Count} tenants", tenants.Count);
        return Results.Ok(tenants);
    }

    public async Task<IResult> GetTenant(string id)
    {
        var repository = new TenantRepository(await _databaseService.GetDatabase());
        var tenant = await repository.GetByIdAsync(id);
        if (tenant == null)
            return Results.NotFound();
        return Results.Ok(tenant);
    }

    public async Task<IResult> GetTenantByTenantId(string tenantId)
    {
        var repository = new TenantRepository(await _databaseService.GetDatabase());
        var tenant = await repository.GetByTenantIdAsync(tenantId);
        if (tenant == null)
            return Results.NotFound();
        return Results.Ok(tenant);
    }
    
    public async Task<IResult> GetTenantByDomain(string domain)
    {
        var repository = new TenantRepository(await _databaseService.GetDatabase());
        var tenant = await repository.GetByDomainAsync(domain);
        if (tenant == null)
            return Results.NotFound();
        return Results.Ok(tenant);
    }

    public async Task<IResult> CreateTenant(CreateTenantRequest request, string createdBy)
    {
        // Validate request
        if (string.IsNullOrEmpty(request.Name))
            return Results.BadRequest("Name is required");
        if (string.IsNullOrEmpty(request.TenantId))
            return Results.BadRequest("TenantId is required");
        if (string.IsNullOrEmpty(request.Domain))
            return Results.BadRequest("Domain is required");

        var repository = new TenantRepository(await _databaseService.GetDatabase());
        
        // Check if tenant with same tenantId or domain already exists
        var existingTenantId = await repository.GetByTenantIdAsync(request.TenantId);
        if (existingTenantId != null)
            return Results.BadRequest($"Tenant with TenantId '{request.TenantId}' already exists");
            
        var existingDomain = await repository.GetByDomainAsync(request.Domain);
        if (existingDomain != null)
            return Results.BadRequest($"Tenant with Domain '{request.Domain}' already exists");

        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = request.TenantId,
            Name = request.Name,
            Domain = request.Domain,
            Description = request.Description,
            Logo = request.Logo,
            Timezone = request.Timezone,
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now
        };

        await repository.CreateAsync(tenant);
        _logger.LogInformation("Created tenant with ID: {Id}", tenant.Id);
        return Results.Ok(tenant);
    }

    public async Task<IResult> UpdateTenant(string id, UpdateTenantRequest request)
    {
        var repository = new TenantRepository(await _databaseService.GetDatabase());
        var tenant = await repository.GetByIdAsync(id);
        
        if (tenant == null)
            return Results.NotFound();
            
        // If domain is being updated, check if it's already in use
        if (!string.IsNullOrEmpty(request.Domain) && request.Domain != tenant.Domain)
        {
            var existingDomain = await repository.GetByDomainAsync(request.Domain);
            if (existingDomain != null)
                return Results.BadRequest($"Domain '{request.Domain}' is already in use");
        }
        
        // Update properties if provided
        if (!string.IsNullOrEmpty(request.Name))
            tenant.Name = request.Name;
            
        if (!string.IsNullOrEmpty(request.Domain))
            tenant.Domain = request.Domain;
            
        if (request.Description != null)
            tenant.Description = request.Description;
            
        if (request.Logo != null)
            tenant.Logo = request.Logo;
            
        if (request.Timezone != null)
            tenant.Timezone = request.Timezone;
            
        tenant.UpdatedAt = DateTime.UtcNow;
        
        var success = await repository.UpdateAsync(id, tenant);
        if (!success)
            return Results.Problem("Failed to update tenant");
            
        _logger.LogInformation("Updated tenant with ID: {Id}", id);
        return Results.Ok(tenant);
    }

    public async Task<IResult> DeleteTenant(string id)
    {
        var repository = new TenantRepository(await _databaseService.GetDatabase());
        var tenant = await repository.GetByIdAsync(id);
        
        if (tenant == null)
            return Results.NotFound();
            
        var success = await repository.DeleteAsync(id);
        if (!success)
            return Results.Problem("Failed to delete tenant");
            
        _logger.LogInformation("Deleted tenant with ID: {Id}", id);
        return Results.Ok();
    }
} 