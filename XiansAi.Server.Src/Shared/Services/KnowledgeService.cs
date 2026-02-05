using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using System.Security;
using System.Text.Json.Serialization;
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
    [JsonPropertyName("activation_name")]
    public string? ActivationName { get; set; }
}

public class DeleteAllVersionsRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("agent")]
    public required string Agent { get; set; }
}

/// <summary>
/// Represents grouped knowledge items for a single knowledge name
/// </summary>
public class KnowledgeGroup
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    
    /// <summary>
    /// Latest system-scoped knowledge (TenantId is null, SystemScoped is true)
    /// </summary>
    [JsonPropertyName("system_scoped")]
    public Knowledge? SystemScoped { get; set; }
    
    /// <summary>
    /// Latest tenant-scoped knowledge where ActivationName is null
    /// </summary>
    [JsonPropertyName("tenant_default")]
    public Knowledge? TenantDefault { get; set; }
    
    /// <summary>
    /// Latest tenant-scoped knowledge for each unique ActivationName
    /// </summary>
    [JsonPropertyName("activations")]
    public List<Knowledge> Activations { get; set; } = new();
}

/// <summary>
/// Response containing all knowledge grouped by name
/// </summary>
public class GroupedKnowledgeResponse
{
    [JsonPropertyName("groups")]
    public List<KnowledgeGroup> Groups { get; set; } = new();
}

public interface IKnowledgeService
{
    Task<ServiceResult<Knowledge>> GetLatestByNameAsync(string name, string agent, string? activationName = null);
    Task<ServiceResult<Knowledge>> GetLatestSystemByNameAsync(string name, string agent);
    Task<IResult> GetById(string id);
    Task<IResult> GetVersions(string name, string? agent);
    Task<IResult> DeleteById(string id);
    Task<IResult> DeleteAllVersions(DeleteAllVersionsRequest request);
    Task<IResult> GetLatestAll(string agent);
    Task<IResult> Create(KnowledgeRequest request);
    Task<IResult> GetLatestByAgent(string agent);
    
    // Admin methods that accept explicit tenantId
    Task<List<Knowledge>> GetAllForTenantAsync(string tenantId, List<string>? agentNames = null);
    Task<Knowledge?> GetByIdForTenantAsync(string id, string tenantId);
    Task<List<Knowledge>> GetVersionsForTenantAsync(string name, string tenantId, string? agentName = null);
    Task<bool> DeleteByIdForTenantAsync(string id, string tenantId);
    Task<bool> DeleteAllVersionsForTenantAsync(string name, string tenantId, string? agentName = null);
    Task<Knowledge> CreateForTenantAsync(string name, string content, string type, string? tenantId, string createdBy, string? agentName = null, string? version = null, string? activationName = null, bool systemScoped = false);
    Task<Knowledge> UpdateForTenantAsync(string knowledgeId, string content, string type, string tenantId, string updatedBy, string? version = null);
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
        // First, try to get tenant-scoped knowledge
        var existingKnowledge = string.IsNullOrEmpty(_tenantContext.TenantId) 
            ? null 
            : await _knowledgeRepository.GetLatestByNameAndTenantAsync<Knowledge>(
                request.Name, request.Agent, _tenantContext.TenantId);
        
        // If not found, try system-scoped
        if (existingKnowledge == null)
        {
            existingKnowledge = await _knowledgeRepository.GetLatestSystemByNameAsync<Knowledge>(request.Name, request.Agent);
        }
        
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

    public async Task<IResult> GetLatestAll(string agent)
    {
        // Validate that the user has access to this agent
        var isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
        
        if (!isSysAdmin)
        {
            var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);
            var agentNames = agents.Select(a => a.Name).ToList();
            
            if (!agentNames.Contains(agent, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Unauthorized access attempt to get knowledge for agent {Agent} by user {UserId}",
                    agent, _tenantContext.LoggedInUser);
                return Results.Json(
                    new { error = "Forbidden", message = "You do not have permission to access knowledge for this agent" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        // Get system-scoped knowledge (TenantId is null and SystemScoped is true) for this agent only
        var systemScopedKnowledge = await _knowledgeRepository.GetUniqueLatestSystemScopedAsync<Knowledge>(new List<string> { agent });
        
        // Get tenant-scoped knowledge grouped by Name+Agent+ActivationName for this agent only
        var tenantScopedKnowledge = await _knowledgeRepository.GetUniqueLatestTenantScopedByActivationAsync<Knowledge>(
            _tenantContext.TenantId, new List<string> { agent });

        // Build grouped response
        // Collect all unique knowledge names from both system and tenant scoped
        var allNames = systemScopedKnowledge.Select(k => k.Name)
            .Union(tenantScopedKnowledge.Select(k => k.Name))
            .Distinct()
            .ToList();

        var groups = new List<KnowledgeGroup>();

        foreach (var name in allNames)
        {
            var group = new KnowledgeGroup { Name = name };

            // 1. System-scoped: Latest where TenantId is null AND SystemScoped is true
            var systemItem = systemScopedKnowledge
                .Where(k => k.Name == name)
                .OrderByDescending(k => k.CreatedAt)
                .FirstOrDefault();
            
            if (systemItem != null)
            {
                systemItem.PermissionLevel = isSysAdmin ? "edit" : "read";
                group.SystemScoped = systemItem;
            }

            // Get tenant-scoped items for this name
            var tenantItems = tenantScopedKnowledge
                .Where(k => k.Name == name)
                .ToList();

            // 2. Tenant default: Latest where ActivationName is null
            var tenantDefault = tenantItems
                .Where(k => string.IsNullOrEmpty(k.ActivationName))
                .OrderByDescending(k => k.CreatedAt)
                .FirstOrDefault();
            
            if (tenantDefault != null)
            {
                tenantDefault.PermissionLevel = GetPermissionLevel(tenantDefault, isSysAdmin, agent);
                group.TenantDefault = tenantDefault;
            }

            // 3. Activations: All unique latest where ActivationName is not null
            var activations = tenantItems
                .Where(k => !string.IsNullOrEmpty(k.ActivationName))
                .ToList();
            
            foreach (var activation in activations)
            {
                activation.PermissionLevel = GetPermissionLevel(activation, isSysAdmin, agent);
            }
            group.Activations = activations;

            groups.Add(group);
        }

        _logger.LogInformation("Found {Count} knowledge groups for agent {Agent}", groups.Count, agent);

        var response = new GroupedKnowledgeResponse { Groups = groups };
        return Results.Ok(response);
    }

    private static string GetPermissionLevel(Knowledge item, bool isSysAdmin, string agentName)
    {
        // System-scoped knowledge can only be edited by system admins
        if (item.SystemScoped)
        {
            return isSysAdmin ? "edit" : "read";
        }
        // Tenant-scoped knowledge can be edited if user has agent permission
        if (!string.IsNullOrWhiteSpace(item.Agent) &&
            string.Equals(item.Agent, agentName, StringComparison.OrdinalIgnoreCase))
        {
            return "edit";
        }
        return "read";
    }

    public async Task<ServiceResult<Knowledge>> GetLatestByNameAsync(string name, string agent, string? activationName = null)
    {
        if (string.IsNullOrWhiteSpace(agent))
        {
            return ServiceResult<Knowledge>.BadRequest("Agent parameter is required");
        }

        // Try 1: If activationName is provided, try with tenantId + agent + activationName
        if (!string.IsNullOrEmpty(activationName))
        {
            var knowledge = await _knowledgeRepository.GetLatestByNameAndActivationAsync<Knowledge>(
                name, agent, _tenantContext.TenantId, activationName);
            
            if (knowledge != null)
            {
                _logger.LogInformation("Found knowledge with tenantId, agent, and activationName");
                return ServiceResult<Knowledge>.Success(knowledge);
            }
        }

        // Try 2: Try with tenantId + agent (without activationName constraint)
        if (!string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            var knowledge = await _knowledgeRepository.GetLatestByNameAndTenantAsync<Knowledge>(
                name, agent, _tenantContext.TenantId);
            
            if (knowledge != null)
            {
                _logger.LogInformation("Found knowledge with tenantId and agent");
                return ServiceResult<Knowledge>.Success(knowledge);
            }
        }

        // Try 3: Try system-scoped (tenantId = null)
        var systemKnowledge = await _knowledgeRepository.GetLatestSystemByNameAsync<Knowledge>(name, agent);
        
        if (systemKnowledge != null)
        {
            _logger.LogInformation("Found knowledge with system scope");
            return ServiceResult<Knowledge>.Success(systemKnowledge);
        }

        return ServiceResult<Knowledge>.NotFound("Knowledge not found");
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
        // Validate that non-system knowledge has a tenant ID
        if (!request.SystemScoped && string.IsNullOrWhiteSpace(_tenantContext.TenantId))
        {
            _logger.LogError("Cannot create non-system knowledge without a tenant ID. User: {UserId}", 
                _tenantContext.LoggedInUser);
            return Results.Json(
                new { error = "BadRequest", message = "Non-system knowledge must be associated with a tenant" },
                statusCode: StatusCodes.Status400BadRequest);
        }

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

        // Check if the knowledge already exists with the same content hash, scope, and activation
        var newContentHash = HashGenerator.GenerateContentHash(request.Content + request.Type);
        var currentLatestKnowledge = await GetLatestByNameAsync(request.Name, request.Agent);

        // If knowledge exists with the same version (content hash), scope, and activation, return it
        // Note: System knowledge (tenant_id=null) and tenant knowledge (tenant_id set) can coexist
        // with the same content because tenant_id differs. The system_scoped field in the unique index
        // provides additional clarity and defense-in-depth data integrity.
        if (currentLatestKnowledge.IsSuccess && 
            currentLatestKnowledge.Data?.Version == newContentHash &&
            currentLatestKnowledge.Data?.SystemScoped == request.SystemScoped &&
            currentLatestKnowledge.Data?.ActivationName == request.ActivationName)
        {
            _logger.LogInformation("Knowledge {Name} already exists with the same content hash, scope, and activation", request.Name);
            return Results.Ok(currentLatestKnowledge.Data);
        }

        // Create the knowledge
        // Note: TenantId is null for system-scoped, and must be set for non-system (validated above)
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
            SystemScoped = request.SystemScoped,
            ActivationName = request.ActivationName
        };

        try
        {
            await _knowledgeRepository.CreateAsync(knowledge);
            return Results.Ok(knowledge);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Handle race condition where knowledge was created between our check and insert
            _logger.LogWarning(ex, "Duplicate key error creating knowledge {Name}. Retrieving existing knowledge.", request.Name);
            
            // Try to get the existing knowledge again
            var existingKnowledge = await GetLatestByNameAsync(request.Name, request.Agent);
            if (existingKnowledge.IsSuccess && existingKnowledge.Data != null)
            {
                _logger.LogInformation("Returning existing knowledge {Name} after duplicate key error", request.Name);
                return Results.Ok(existingKnowledge.Data);
            }
            
            // If we still can't find it, throw the original exception
            throw;
        }
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

    // Admin methods that accept explicit tenantId (no permission checks - handled by endpoints)
    
    public async Task<List<Knowledge>> GetAllForTenantAsync(string tenantId, List<string>? agentNames = null)
    {
        if (agentNames == null || agentNames.Count == 0)
        {
            // Get all knowledge for the tenant if no agent filter
            return await _knowledgeRepository.GetAllAsync<Knowledge>(tenantId);
        }
        else
        {
            // Get filtered knowledge by agent names
            return await _knowledgeRepository.GetUniqueLatestAsync<Knowledge>(tenantId, agentNames);
        }
    }

    public async Task<Knowledge?> GetByIdForTenantAsync(string id, string tenantId)
    {
        var knowledge = await _knowledgeRepository.GetByIdAsync<Knowledge>(id);
        
        // Verify knowledge belongs to the specified tenant
        if (knowledge != null && knowledge.TenantId != tenantId)
        {
            return null;
        }
        
        return knowledge;
    }

    public async Task<List<Knowledge>> GetVersionsForTenantAsync(string name, string tenantId, string? agentName = null)
    {
        return await _knowledgeRepository.GetByNameAsync<Knowledge>(name, agentName, tenantId);
    }

    public async Task<bool> DeleteByIdForTenantAsync(string id, string tenantId)
    {
        var knowledge = await _knowledgeRepository.GetByIdAsync<Knowledge>(id);
        
        // Verify knowledge belongs to the specified tenant
        if (knowledge == null || knowledge.TenantId != tenantId)
        {
            return false;
        }
        
        return await _knowledgeRepository.DeleteAsync<Knowledge>(id);
    }

    public async Task<bool> DeleteAllVersionsForTenantAsync(string name, string tenantId, string? agentName = null)
    {
        return await _knowledgeRepository.DeleteAllVersionsAsync<Knowledge>(name, agentName, tenantId);
    }

    public async Task<Knowledge> CreateForTenantAsync(
        string name, 
        string content, 
        string type, 
        string? tenantId, 
        string createdBy, 
        string? agentName = null, 
        string? version = null,
        string? activationName = null,
        bool systemScoped = false)
    {
        // Generate version hash if not provided
        if (string.IsNullOrWhiteSpace(version))
        {
            version = HashGenerator.GenerateContentHash(content + type);
        }

        var knowledge = new Knowledge
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = name,
            Content = content,
            Type = type,
            Version = version,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            TenantId = tenantId,
            Agent = agentName,
            SystemScoped = systemScoped,
            ActivationName = activationName
        };

        await _knowledgeRepository.CreateAsync(knowledge);
        return knowledge;
    }

    public async Task<Knowledge> UpdateForTenantAsync(
        string knowledgeId, 
        string content, 
        string type, 
        string tenantId, 
        string updatedBy, 
        string? version = null)
    {
        // Get existing knowledge to preserve name and agent
        var existingKnowledge = await _knowledgeRepository.GetByIdAsync<Knowledge>(knowledgeId);
        if (existingKnowledge == null)
        {
            throw new InvalidOperationException("Knowledge not found");
        }

        // Verify knowledge belongs to the specified tenant
        if (existingKnowledge.TenantId != tenantId)
        {
            throw new InvalidOperationException("Knowledge does not belong to this tenant");
        }

        // Generate version hash if not provided
        if (string.IsNullOrWhiteSpace(version))
        {
            version = HashGenerator.GenerateContentHash(content + type);
        }

        // Create new version (maintains version history)
        var updatedKnowledge = new Knowledge
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = existingKnowledge.Name,
            Content = content,
            Type = type,
            Version = version,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = updatedBy,
            TenantId = tenantId,
            Agent = existingKnowledge.Agent,
            SystemScoped = false,
            ActivationName = existingKnowledge.ActivationName  // Preserve activation name
        };

        await _knowledgeRepository.CreateAsync(updatedKnowledge);
        return updatedKnowledge;
    }
}