using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Features.AgentApi.Repositories;
using MongoDB.Driver;
using XiansAi.Server.GenAi;
using Shared.Auth;
using Shared.Data.Models;
using XiansAi.Server.Features.WebApi.Models;
using Shared.Data;
using Shared.Utils.GenAi;

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
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMarkdownService _markdownService; 
    public DefinitionsService(
        IFlowDefinitionRepository flowDefinitionRepository,
        ILogger<DefinitionsService> logger,
        IOpenAIClientService openAIClientService,
        ITenantContext tenantContext,
        IMarkdownService markdownService
    )
    {
        _flowDefinitionRepository = flowDefinitionRepository;
        _logger = logger;
        _openAIClientService = openAIClientService;
        _tenantContext = tenantContext;
        _markdownService = markdownService;
    }

    public async Task<IResult> CreateAsync(FlowDefinitionRequest request)
    {
        var existingDefinition = await _flowDefinitionRepository.GetByWorkflowTypeAsync(request.WorkflowType);
        var definition = CreateFlowDefinitionFromRequest(request, existingDefinition);
        
        if (existingDefinition != null)
        {
            if (!existingDefinition.Permissions.HasPermission(
                _tenantContext.LoggedInUser ?? throw new InvalidOperationException("No logged in user found"),
                _tenantContext.UserRoles, 
                PermissionLevel.Write))
            {
                _logger.LogWarning("User {User} attempted to update definition {WorkflowType} without write permission", 
                    _tenantContext.LoggedInUser, definition.WorkflowType);
                return Results.Json(
                    new { message = "You do not have permission to update this definition." }, 
                    statusCode: StatusCodes.Status403Forbidden
                );
            }

            if (existingDefinition.Hash != definition.Hash)
            {
                await GenerateMarkdown(definition);
                await _flowDefinitionRepository.UpdateAsync(existingDefinition.Id, definition);
                return Results.Ok("Definition updated successfully");
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
        
        var permissions = existingDefinition?.Permissions ?? new Permission();
        if (existingDefinition == null)
        {
            permissions.GrantOwnerAccess(userId);
        }
        
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
            UpdatedAt = DateTime.UtcNow,
            Permissions = permissions
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
