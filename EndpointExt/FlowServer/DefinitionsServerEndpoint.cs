using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using XiansAi.Server.MongoDB.Repositories;
using XiansAi.Server.MongoDB.Models;
using XiansAi.Server.MongoDB;
using MongoDB.Driver;
using XiansAi.Server.GenAi;

namespace XiansAi.Server.EndpointExt.FlowServer;



public class FlowDefinitionRequest
{
    [Required]
    [JsonPropertyName("typeName")]
    public required string TypeName { get; set; }

    [Required]
    [JsonPropertyName("className")]
    public required string ClassName { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }

    [Required]
    [JsonPropertyName("activities")]
    public required List<ActivityDefinitionRequest> Activities { get; set; }

    [Required]
    [JsonPropertyName("parameters")]
    public required List<ParameterRequest> Parameters { get; set; }
}

public class ActivityDefinitionRequest
{
    [Required]
    [JsonPropertyName("activityName")]
    public required string ActivityName { get; set; }

    [JsonPropertyName("agentNames")]
    public required List<string> AgentNames { get; set; }

    [JsonPropertyName("instructions")]
    public required List<string> Instructions { get; set; }

    [JsonPropertyName("parameters")]
    public required List<ParameterRequest> Parameters { get; set; }
}

public class ParameterRequest
{
    [Required]
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [Required]
    [JsonPropertyName("type")]
    public required string Type { get; set; }
}

public class DefinitionsServerEndpoint
{
    private readonly ILogger<DefinitionsServerEndpoint> _logger;
    private readonly IOpenAIClientService _openAIClientService;
    private readonly IDatabaseService _databaseService;
    public DefinitionsServerEndpoint(
        IDatabaseService databaseService,
        ILogger<DefinitionsServerEndpoint> logger,
        IOpenAIClientService openAIClientService    
    )
    {
        _databaseService = databaseService;
        _logger = logger;
        _openAIClientService = openAIClientService;
    }

    public async Task<IResult> CreateAsync(FlowDefinitionRequest request)
    {
        var definition = CreateFlowDefinitionFromRequest(request);
        var definitionRepository = new FlowDefinitionRepository(await _databaseService.GetDatabase());
        // Find existing definition with the same hash
        var existingDefinition = await definitionRepository.GetByHashAsync(definition.Hash);
        
        // Only proceed if:
        // 1. No existing definition with same hash exists, OR
        // 2. New definition has markdown AND existing definition has no markdown
        if (existingDefinition == null || 
            (!string.IsNullOrEmpty(definition.Markdown) && string.IsNullOrEmpty(existingDefinition.Markdown)))
        {
            if (existingDefinition != null)
            {
                // Delete the existing record first to avoid duplicate key errors
                await definitionRepository.DeleteAsync(existingDefinition.Id);
            }
            await GenerateMarkdown(definition);
            await definitionRepository.CreateAsync(definition);
            return Results.Ok("Definition created successfully");
        }
        
        return Results.Ok("Definition already exists");
    }

    private async Task GenerateMarkdown(FlowDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.Source))
        {
            return;
        }
        var markdownGenerator = new MarkdownGenerator(_openAIClientService, _logger);
#pragma warning disable CS8601 // Possible null reference assignment.
        definition.Markdown = await markdownGenerator.GenerateMarkdown(definition.Source);
#pragma warning restore CS8601 // Possible null reference assignment.
    }

    private FlowDefinition CreateFlowDefinitionFromRequest(FlowDefinitionRequest request)
    {
        return new FlowDefinition
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TypeName = request.TypeName,
            ClassName = request.ClassName,
            Hash = ComputeHash(JsonSerializer.Serialize(request)),
            Source = string.IsNullOrEmpty(request.Source) ? string.Empty : request.Source,
            Markdown = string.IsNullOrEmpty(request.Markdown) ? string.Empty : request.Markdown,
            Activities = request.Activities.Select(a => new ActivityDefinition
            {
                ActivityName = a.ActivityName,
                AgentNames = a.AgentNames,
                Instructions = a.Instructions,
                Parameters = a.Parameters.Select(p => new ParameterDefinition
                {
                    Name = p.Name,
                    Type = p.Type
                }).ToList()
            }).ToList(),
            Parameters = request.Parameters.Select(p => new ParameterDefinition
            {
                Name = p.Name,
                Type = p.Type
            }).ToList(),
            CreatedAt = DateTime.UtcNow
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
