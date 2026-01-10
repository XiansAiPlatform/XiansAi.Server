using MongoDB.Bson;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using System.Security;
using System.Text.Json.Serialization;
using System.Text.Json;
using Shared.Utils.Services;

namespace Shared.Services;

public class KnowledgeRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("content")]
    public required string Content { get; set; }
    [JsonPropertyName("type")]
    public required string Type { get; set; }
    [JsonPropertyName("agent")]
    public required string Agent { get; set; }
    [JsonPropertyName("system_scoped")]
    public bool SystemScoped { get; set; } = false;
}

public class DeleteAllVersionsRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("agent")]
    public required string Agent { get; set; }
}

public interface IKnowledgeService
{
    Task<ServiceResult<Knowledge>> GetLatestByNameAsync(string name, string agent);
    Task<ServiceResult<Knowledge>> GetLatestSystemByNameAsync(string name, string agent);
    Task<IResult> GetById(string id);
    Task<IResult> GetVersions(string name, string? agent);
    Task<IResult> DeleteById(string id);
    Task<IResult> DeleteAllVersions(DeleteAllVersionsRequest request);
    Task<IResult> GetLatestAll(string? scope = null);
    Task<IResult> Create(KnowledgeRequest request);
    Task<IResult> GetLatestByAgent(string agent);
}

public class KnowledgeService : IKnowledgeService
{
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly ILogger<KnowledgeService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IAgentRepository _agentRepository;
    
    public KnowledgeService(
        IKnowledgeRepository knowledgeRepository,
        ILogger<KnowledgeService> logger,
        ITenantContext tenantContext,
        IAgentRepository agentRepository
    )
    {
        _knowledgeRepository = knowledgeRepository;
        _logger = logger;
        _tenantContext = tenantContext;
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
    }

    // Validate that knowledge belongs to the user's tenant or is global
    private void ValidateKnowledgeTenant(IKnowledge knowledge)
    {
        if (knowledge.TenantId != null && knowledge.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning("Unauthorized access attempt to knowledge {Id} from tenant {TenantId}",
                knowledge.Id, _tenantContext.TenantId);
            throw new SecurityException("Access denied: Knowledge does not belong to this tenant");
        }
    }

    private async Task<List<string>> GetUserAgentNamesAsync()
    {
        var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);
        return agents.Select(a => a.Name).ToList();
    }

    public async Task<IResult> GetById(string id)
    {
        var knowledge = await _knowledgeRepository.GetByIdAsync<Knowledge>(id);
        
        if (knowledge == null)
            return Results.NotFound("Knowledge not found");

        var isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
        
        // System-scoped knowledge can only be edited by system admins
        if (knowledge.SystemScoped)
        {
            knowledge.PermissionLevel = isSysAdmin ? "edit" : "read";
        }
        else
        {
            // Tenant-scoped knowledge can be edited if user has agent permission
            var agentNames = await GetUserAgentNamesAsync();
            if (!string.IsNullOrWhiteSpace(knowledge.Agent) &&
                agentNames.Contains(knowledge.Agent, StringComparer.OrdinalIgnoreCase))
            {
                knowledge.PermissionLevel = "edit";
            }
            else
            {
                knowledge.PermissionLevel = "read";
            }
        }

        try
        {
            ValidateKnowledgeTenant(knowledge);
            return Results.Ok(knowledge);
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning(ex, "Security exception in GetById");
            return Results.Forbid();
        }
    }

    public async Task<IResult> GetVersions(string name, string? agent)
    {
        var versions = await _knowledgeRepository.GetByNameAsync<Knowledge>(name, agent, _tenantContext.TenantId);
        return Results.Ok(versions);
    }

    public async Task<IResult> DeleteById(string id)
    {
        var knowledge = await _knowledgeRepository.GetByIdAsync<Knowledge>(id);
        if (knowledge == null)
            return Results.NotFound("Knowledge not found");

        // Check if it's system-scoped knowledge
        if (knowledge.SystemScoped)
        {
            // Only system admins can delete system-scoped knowledge
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                _logger.LogWarning("Unauthorized access attempt to delete system-scoped knowledge by user {UserId}",
                    _tenantContext.LoggedInUser);
                return Results.Json(
                    new { error = "Forbidden", message = "Only system administrators can delete system-scoped knowledge" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }
        else
        {
            // For tenant-scoped knowledge, check agent permissions (system admins bypass this check)
            var isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
            if (!isSysAdmin)
            {
                var agentNames = await GetUserAgentNamesAsync();
                if (!agentNames.Contains(knowledge.Agent, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Unauthorized access attempt to delete knowledge for agent {Agent} by user {UserId}",
                        knowledge.Agent, _tenantContext.LoggedInUser);
                    return Results.Json(
                        new { error = "Forbidden", message = "You do not have permission to delete this knowledge" },
                        statusCode: StatusCodes.Status403Forbidden);
                }
            }
        }

        try
        {
            ValidateKnowledgeTenant(knowledge);

            var result = await _knowledgeRepository.DeleteAsync<Knowledge>(id);
            if (!result)
                return Results.NotFound("Knowledge not found or could not be deleted");

            return Results.Ok();
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning(ex, "Security exception in DeleteById");
            return Results.Unauthorized();
        }
    }

    public async Task<IResult> DeleteAllVersions(DeleteAllVersionsRequest request)
    {
        // First, get the latest knowledge to check if it's system-scoped
        var existingKnowledge = await _knowledgeRepository.GetLatestByNameAsync<Knowledge>(request.Name, request.Agent, _tenantContext.TenantId);
        
        if (existingKnowledge == null)
            return Results.NotFound("Knowledge not found");

        // Check if it's system-scoped knowledge
        if (existingKnowledge.SystemScoped)
        {
            // Only system admins can delete system-scoped knowledge
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                _logger.LogWarning("Unauthorized access attempt to delete system-scoped knowledge by user {UserId}",
                    _tenantContext.LoggedInUser);
                return Results.Json(
                    new { error = "Forbidden", message = "Only system administrators can delete system-scoped knowledge" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }
        else
        {
            // For tenant-scoped knowledge, check agent permissions (system admins bypass this check)
            var isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
            if (!isSysAdmin)
            {
                var agentNames = await GetUserAgentNamesAsync();
                if (!agentNames.Contains(request.Agent, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Unauthorized access attempt to delete knowledge for agent {Agent} by user {UserId}",
                        request.Agent, _tenantContext.LoggedInUser);
                    return Results.Json(
                        new { error = "Forbidden", message = "You do not have permission to delete knowledge for this agent" },
                        statusCode: StatusCodes.Status403Forbidden);
                }
            }
        }

        // Delete with appropriate tenantId (null for system-scoped, tenantId for tenant-scoped)
        var tenantIdToDelete = existingKnowledge.SystemScoped ? null : _tenantContext.TenantId;
        var result = await _knowledgeRepository.DeleteAllVersionsAsync<Knowledge>(request.Name, request.Agent, tenantIdToDelete);
        
        if (!result)
            return Results.NotFound("Knowledge not found or could not be deleted");

        return Results.Ok(new { message = "All versions deleted" });
    }

    public async Task<IResult> GetLatestAll(string? scope = null)
    {
        var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);
        var agentNames = agents.Select(a => a.Name).ToList();

        List<Knowledge> knowledge;
        
        // Filter by scope if specified
        if (string.Equals(scope, "System", StringComparison.OrdinalIgnoreCase))
        {
            // Get only system-scoped knowledge (TenantId is null and SystemScoped is true)
            knowledge = await _knowledgeRepository.GetUniqueLatestSystemScopedAsync<Knowledge>(agentNames);
        }
        else if (string.Equals(scope, "Tenant", StringComparison.OrdinalIgnoreCase))
        {
            // Get only tenant-scoped knowledge (TenantId matches current tenant)
            knowledge = await _knowledgeRepository.GetUniqueLatestTenantScopedAsync<Knowledge>(_tenantContext.TenantId, agentNames);
        }
        else
        {
            // Default: get all knowledge (both system and tenant scoped)
            knowledge = await _knowledgeRepository.GetUniqueLatestAsync<Knowledge>(_tenantContext.TenantId, agentNames);
        }

        _logger.LogInformation("Found {Count} knowledge items for scope: {Scope}", knowledge.Count, scope ?? "all");

        var isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);

        foreach (var item in knowledge)
        {
            // System-scoped knowledge can only be edited by system admins
            if (item.SystemScoped)
            {
                item.PermissionLevel = isSysAdmin ? "edit" : "read";
            }
            // Tenant-scoped knowledge can be edited if user has agent permission
            else if (!string.IsNullOrWhiteSpace(item.Agent) &&
                agentNames.Contains(item.Agent, StringComparer.OrdinalIgnoreCase))
            {
                item.PermissionLevel = "edit";
            }
            else
            {
                item.PermissionLevel = "read";
            }
        }
        return Results.Ok(knowledge);
    }

    public async Task<ServiceResult<Knowledge>> GetLatestByNameAsync(string name, string agent)
    {
        var knowledge = await _knowledgeRepository.GetLatestByNameAsync<Knowledge>(name, agent, _tenantContext.TenantId);
        if (knowledge == null)
            return ServiceResult<Knowledge>.NotFound("Knowledge not found");

        return ServiceResult<Knowledge>.Success(knowledge);
    }

    public async Task<ServiceResult<Knowledge>> GetLatestSystemByNameAsync(string name, string agent)
    {
        var knowledge = await _knowledgeRepository.GetLatestSystemByNameAsync<Knowledge>(name, agent);
        if (knowledge == null || knowledge.TenantId != null || !knowledge.SystemScoped)
            return ServiceResult<Knowledge>.NotFound("System knowledge not found");

        return ServiceResult<Knowledge>.Success(knowledge);
    }

    public async Task<IResult> Create(KnowledgeRequest request)
    {
        // Check system admin permission for system-scoped knowledge
        if (request.SystemScoped && !_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
        {
            _logger.LogWarning("Unauthorized access attempt to create system-scoped knowledge by user {UserId}",
                _tenantContext.LoggedInUser);
            return Results.Json(
                new { error = "Forbidden", message = "Only system administrators can create system-scoped knowledge" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // System admins have access to all agents
        var isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
        if (!isSysAdmin)
        {
            var agentNames = await GetUserAgentNamesAsync();
            if (!agentNames.Contains(request.Agent, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Unauthorized access attempt to create knowledge for agent {Agent} by user {UserId}",
                    request.Agent, _tenantContext.LoggedInUser);
                return Results.Json(
                    new { error = "Forbidden", message = "You do not have permission to create knowledge for this agent" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        // Check if the knowledge already exists with the same content hash and SystemScoped flag
        var newContentHash = HashGenerator.GenerateContentHash(request.Content + request.Type);
        var currentLatestKnowledge = await GetLatestByNameAsync(request.Name, request.Agent);

        if (currentLatestKnowledge.IsSuccess && 
            currentLatestKnowledge.Data?.Version == newContentHash &&
            currentLatestKnowledge.Data?.SystemScoped == request.SystemScoped)
        {
            _logger.LogInformation("Knowledge {Name} already exists with the same content hash and scope", request.Name);
            return Results.Ok(currentLatestKnowledge.Data);
        }

        // Create the knowledge
        var knowledge = new Knowledge
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = request.Name,
            Content = request.Content,
            Type = request.Type,
            Version = newContentHash,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            TenantId = request.SystemScoped ? null : _tenantContext.TenantId,
            Agent = request.Agent,
            SystemScoped = request.SystemScoped
        };

        await _knowledgeRepository.CreateAsync(knowledge);
        return Results.Ok(knowledge);
    }

    public async Task<IResult> GetLatestByAgent(string agent)
    {
        // System admins have access to all agents
        var isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
        List<string> agentNames = new List<string>();
        
        if (!isSysAdmin)
        {
            agentNames = await GetUserAgentNamesAsync();
            if (!agentNames.Contains(agent, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Unauthorized access attempt to list knowledge for agent {Agent} by user {UserId}",
                    agent, _tenantContext.LoggedInUser);
                return Results.Json(
                    new { error = "Forbidden", message = "You do not have permission to list knowledge for this agent" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        var knowledge = await _knowledgeRepository.GetUniqueLatestAsync<Knowledge>(_tenantContext.TenantId, new List<string> { agent });

        _logger.LogInformation("Found {Count} knowledge items for agent {Agent}", knowledge.Count, agent);
        
        foreach (var item in knowledge)
        {
            // System-scoped knowledge can only be edited by system admins
            if (item.SystemScoped)
            {
                item.PermissionLevel = isSysAdmin ? "edit" : "read";
            }
            // Tenant-scoped knowledge can be edited if user has agent permission
            else if (!string.IsNullOrWhiteSpace(item.Agent) &&
                (isSysAdmin || agentNames.Contains(item.Agent, StringComparer.OrdinalIgnoreCase)))
            {
                item.PermissionLevel = "edit";
            }
            else
            {
                item.PermissionLevel = "read";
            }
        }
        return Results.Ok(knowledge);
    }
}