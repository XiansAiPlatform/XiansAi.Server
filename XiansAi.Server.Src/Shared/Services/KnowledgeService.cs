using MongoDB.Bson;
using Shared.Auth;
using XiansAi.Server.Shared.Data.Models;
using XiansAi.Server.Shared.Repositories;
using XiansAi.Server.Utils;
using System.Security;
using System.Text.Json.Serialization;

namespace XiansAi.Server.Shared.Services;

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
    Task<IResult> GetLatestByName(string name, string agent);
    Task<IResult> GetById(string id);
    Task<IResult> GetVersions(string name, string? agent);
    Task<IResult> DeleteById(string id);
    Task<IResult> DeleteAllVersions(DeleteAllVersionsRequest request);
    Task<IResult> GetLatestAll();
    Task<IResult> GetAll();
    Task<IResult> Create(KnowledgeRequest request);
}

public class KnowledgeService : IKnowledgeService
{
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly ILogger<KnowledgeService> _logger;
    private readonly ITenantContext _tenantContext;
    public KnowledgeService(
        IKnowledgeRepository knowledgeRepository,
        ILogger<KnowledgeService> logger,
        ITenantContext tenantContext
    )
    {
        _knowledgeRepository = knowledgeRepository;
        _logger = logger;
        _tenantContext = tenantContext;
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

    public async Task<IResult> GetById(string id)
    {
        var knowledge = await _knowledgeRepository.GetByIdAsync<Knowledge>(id);
        if (knowledge == null)
            return Results.NotFound("Knowledge not found");
        
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
        // Only allow deletion of tenant-specific knowledge
        var result = await _knowledgeRepository.DeleteAllVersionsAsync<Knowledge>(request.Name, request.Agent, _tenantContext.TenantId);
        if (!result)
            return Results.NotFound("Knowledge not found or could not be deleted");
        
        return Results.Ok(new { message = "All versions deleted" });
    }

    public async Task<IResult> GetLatestAll()
    {
        var knowledge = await _knowledgeRepository.GetUniqueLatestAsync<Knowledge>(_tenantContext.TenantId);
        _logger.LogInformation("Found {Count} knowledge items", knowledge.Count);
        return Results.Ok(knowledge);
    }

    public async Task<IResult> GetLatestByName(string name, string agent)
    {
        var knowledge = await _knowledgeRepository.GetLatestByNameAsync<Knowledge>(name, agent, _tenantContext.TenantId);
        if (knowledge == null)
            return Results.NotFound("Knowledge not found");
        
        return Results.Ok(knowledge);
    }

    public async Task<IResult> GetAll()
    {
        var knowledge = await _knowledgeRepository.GetAllAsync<Knowledge>(_tenantContext.TenantId);
        _logger.LogInformation("Found {Count} knowledge items", knowledge.Count);
        return Results.Ok(knowledge);
    }

    public async Task<IResult> Create(KnowledgeRequest request)
    {
        var knowledge = new Knowledge
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = request.Name,
            Content = request.Content,
            Type = request.Type,
            Version = HashGenerator.GenerateContentHash(ObjectId.GenerateNewId().ToString() + DateTime.UtcNow.ToString()),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            TenantId = _tenantContext.TenantId,
            Agent = request.Agent
        };
        
        await _knowledgeRepository.CreateAsync(knowledge);
        return Results.Ok(knowledge);
    }
} 