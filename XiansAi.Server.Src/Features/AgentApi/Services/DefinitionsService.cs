using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Features.AgentApi.Repositories;
using MongoDB.Driver;
using XiansAi.Server.GenAi;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Data;
using Shared.Utils.GenAi;
using Features.WebApi.Repositories;
using Shared.Repositories;

namespace Features.AgentApi.Services.Lib;

public class FlowDefinitionRequest
{
    [JsonPropertyName("agent")]
    public string? Agent { get; set; }
    
    [Required]
    [JsonPropertyName("workflowType")]
    public required string WorkflowType { get; set; }


    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [Required]
    [JsonPropertyName("activityDefinitions")]
    public required List<ActivityDefinitionRequest> ActivityDefinitions { get; set; }

    [Required]
    [JsonPropertyName("parameterDefinitions")]
    public required List<ParameterDefinitionRequest> ParameterDefinitions { get; set; }
}

public class ActivityDefinitionRequest
{
    [Required]
    [JsonPropertyName("activityName")]
    public required string ActivityName { get; set; }

    [JsonPropertyName("agentToolNames")]
    public List<string>? AgentToolNames { get; set; }

    [JsonPropertyName("knowledgeIds")]
    public required List<string> KnowledgeIds { get; set; }

    [JsonPropertyName("parameterDefinitions")]
    public required List<ParameterDefinition> ParameterDefinitions { get; set; }
}

public class ParameterDefinitionRequest
{
    [Required]
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [Required]
    [JsonPropertyName("type")]
    public required string Type { get; set; }
}

public interface IDefinitionsService
{
    Task<IResult> CreateAsync(FlowDefinitionRequest request);
}

public class DefinitionsService : IDefinitionsService
{
    private readonly ILogger<DefinitionsService> _logger;
    private readonly IOpenAIClientService _openAIClientService;
    private readonly Repositories.IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMarkdownService _markdownService; 
    private readonly IAgentPermissionRepository _agentPermissionRepository;
    
    public DefinitionsService(
        Repositories.IFlowDefinitionRepository flowDefinitionRepository,
        IAgentRepository agentRepository,
        ILogger<DefinitionsService> logger,
        IOpenAIClientService openAIClientService,
        ITenantContext tenantContext,
        IMarkdownService markdownService,
        IAgentPermissionRepository agentPermissionRepository
    )
    {
        _flowDefinitionRepository = flowDefinitionRepository;
        _agentRepository = agentRepository;
        _logger = logger;
        _openAIClientService = openAIClientService;
        _tenantContext = tenantContext;
        _markdownService = markdownService;
        _agentPermissionRepository = agentPermissionRepository;
    }

    public async Task<IResult> CreateAsync(FlowDefinitionRequest request)
    {
        if (string.IsNullOrEmpty(request.Agent))
        {
            _logger.LogWarning("Agent name is empty or null");
            return Results.Json(
                new { message = "Agent name is required." }, 
                statusCode: StatusCodes.Status400BadRequest);
        }

        var currentUser = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("No logged in user found");
        
        // Ensure agent exists using thread-safe upsert operation
        var agent = await _agentRepository.UpsertAgentAsync(request.Agent, _tenantContext.TenantId, currentUser);
        
        // Check if the user has permissions for this agent
        var permissions = await _agentPermissionRepository.GetAgentPermissionsAsync(request.Agent);
        _logger.LogInformation("Permissions: {Permissions}", permissions);
        
        if (permissions != null && !permissions.HasPermission(currentUser, _tenantContext.UserRoles, PermissionLevel.Write))
        {
            var warningMessage = @$"User `{currentUser}` does not have write permission 
                for agent `{request.Agent}` which is owned by another user. 
                Please use a different name or ask the owner to share 
                the agent with you with write permission.";
            _logger.LogWarning(warningMessage);
            return Results.Json(
                new { message = warningMessage }, 
                statusCode: StatusCodes.Status403Forbidden);
        }
        
        var existingDefinition = await _flowDefinitionRepository.GetByWorkflowTypeAsync(request.WorkflowType);
        var definition = CreateFlowDefinitionFromRequest(request, existingDefinition);
        
        if (existingDefinition != null)
        {
            if (existingDefinition.Hash != definition.Hash)
            {
                _logger.LogInformation("Flow definition with workflow type {WorkflowType} has changed hash. Deleting existing and creating new one.", request.WorkflowType);
                await _flowDefinitionRepository.DeleteAsync(existingDefinition.Id);
                await GenerateMarkdown(definition);
                // Create new definition with fresh ID
                definition.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                await _flowDefinitionRepository.CreateAsync(definition);
                return Results.Ok("Definition deleted and recreated successfully");
            }
            
            return Results.Ok("Definition already up to date");
        }

        _logger.LogInformation("Creating new definition {WorkflowType}", definition.WorkflowType);
        await GenerateMarkdown(definition);
        await _flowDefinitionRepository.CreateAsync(definition);
        return Results.Ok("New definition created successfully");
    }

    private async Task GenerateMarkdown(FlowDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.Source))
        {
            return;
        }
        definition.Markdown = await _markdownService.GenerateMarkdown(definition.Source);
        definition.UpdatedAt = DateTime.UtcNow;
    }

    private FlowDefinition CreateFlowDefinitionFromRequest(FlowDefinitionRequest request, FlowDefinition? existingDefinition = null)
    {
        var userId = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("No logged in user found. Check the certificate.");
        
        return new FlowDefinition
        {
            Id = existingDefinition?.Id ?? ObjectId.GenerateNewId().ToString(),
            WorkflowType = request.WorkflowType,
            Agent = request.Agent ?? request.WorkflowType,
            Hash = ComputeHash(JsonSerializer.Serialize(request)),
            Source = string.IsNullOrEmpty(request.Source) ? string.Empty : request.Source,
            Markdown = string.IsNullOrEmpty(existingDefinition?.Markdown) ? string.Empty : existingDefinition.Markdown,
            ActivityDefinitions = request.ActivityDefinitions.Select(a => new ActivityDefinition
            {
                ActivityName = a.ActivityName,
                AgentToolNames = a.AgentToolNames,
                KnowledgeIds = a.KnowledgeIds,
                ParameterDefinitions = a.ParameterDefinitions.Select(p => new ParameterDefinition
                {
                    Name = p.Name,
                    Type = p.Type
                }).ToList()
            }).ToList(),
            ParameterDefinitions = request.ParameterDefinitions.Select(p => new ParameterDefinition
            {
                Name = p.Name,
                Type = p.Type
            }).ToList(),
            CreatedAt = existingDefinition?.CreatedAt ?? DateTime.UtcNow,
            CreatedBy = existingDefinition?.CreatedBy ?? userId,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private string ComputeHash(string source)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(source);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLower();
    }
}
