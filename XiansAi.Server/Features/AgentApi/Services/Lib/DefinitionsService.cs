using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Features.AgentApi.Data.Repositories;
using MongoDB.Driver;
using XiansAi.Server.GenAi;
using Features.Shared.Auth;

namespace Features.AgentApi.Services.Lib;

public class FlowDefinitionRequest
{
    private string? _agentName;
    [JsonPropertyName("agentName")]
    public string? AgentName { 
        get {
            if (string.IsNullOrEmpty(_agentName)) {
                return TypeName;
            }
            return _agentName;
        }
        set {
            _agentName = value;
        }

    }
    [Required]
    [JsonPropertyName("typeName")]
    public required string TypeName { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }

    [Required]
    [JsonPropertyName("activities")]
    public required List<ActivityDefinition> Activities { get; set; }

    [Required]
    [JsonPropertyName("parameters")]
    public required List<ParameterRequest> Parameters { get; set; }
}

public class ActivityDefinitionRequest
{
    [Required]
    [JsonPropertyName("activityName")]
    public required string ActivityName { get; set; }

    [JsonPropertyName("agentToolNames")]
    public List<string>? AgentToolNames { get; set; }

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

public class DefinitionsService
{
    private readonly ILogger<DefinitionsService> _logger;
    private readonly IOpenAIClientService _openAIClientService;
    private readonly FlowDefinitionRepository _flowDefinitionRepository;
    private readonly ITenantContext _tenantContext;
    
    public DefinitionsService(
        FlowDefinitionRepository flowDefinitionRepository,
        ILogger<DefinitionsService> logger,
        IOpenAIClientService openAIClientService,
        ITenantContext tenantContext
    )
    {
        _flowDefinitionRepository = flowDefinitionRepository;
        _logger = logger;
        _openAIClientService = openAIClientService;
        _tenantContext = tenantContext;
    }

    public async Task<IResult> CreateAsync(FlowDefinitionRequest request)
    {
        var definition = CreateFlowDefinitionFromRequest(request);

        // IF another user has the same type name, we reject the request
        var ownedByAnother = await _flowDefinitionRepository.IfExistsInAnotherOwner(definition.TypeName, definition.Owner);
        if (ownedByAnother)
        {
            _logger.LogInformation("Another user has already used this flow type name {typeName}. Rejecting request", definition.TypeName);
            return Results.BadRequest($"Another user has already used this flow type name {definition.TypeName}. Please choose a different flow name.");
        }

        
        // Find existing definition with the same hash
        var existingDefinition = await _flowDefinitionRepository.GetLatestByTypeNameAndOwnerAsync(definition.TypeName, definition.Owner);

        // no definition in database
        if (existingDefinition == null)
        {
            _logger.LogInformation("No existing definition found, creating new definition");
            await GenerateMarkdown(definition);
            await _flowDefinitionRepository.CreateAsync(definition);
            return Results.Ok("No existing definition found, new definition created successfully");
        }

        // no markdown in the existing record, but there is a markdown in the new definition
        if (string.IsNullOrEmpty(existingDefinition.Markdown) && !string.IsNullOrEmpty(definition.Markdown))
        {
            await GenerateMarkdown(definition);
            await _flowDefinitionRepository.CreateAsync(definition);
            // delete the old definition
            await _flowDefinitionRepository.DeleteAsync(existingDefinition.Id);
            return Results.Ok("No markdown in the existing definition, new definition created successfully");
        }

        if (existingDefinition.Hash != definition.Hash)
        {
            await GenerateMarkdown(definition);
            await _flowDefinitionRepository.CreateAsync(definition);
            // delete the old definition
            await _flowDefinitionRepository.DeleteAsync(existingDefinition.Id);
            return Results.Ok("Definition had a different hash, new definition created successfully");
        }  else {
            return Results.Ok("Definition already up to date");
        }
    }

    private async Task GenerateMarkdown(FlowDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.Source))
        {
            return;
        }
        var markdownGenerator = new MarkdownGenerator(_openAIClientService, _logger);
        definition.Markdown = await markdownGenerator.GenerateMarkdown(definition.Source) ?? string.Empty;
    }

    private FlowDefinition CreateFlowDefinitionFromRequest(FlowDefinitionRequest request)
    {
        return new FlowDefinition
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TypeName = request.TypeName,
            AgentName = request.AgentName ?? request.TypeName,
            Hash = ComputeHash(JsonSerializer.Serialize(request)),
            Source = string.IsNullOrEmpty(request.Source) ? string.Empty : request.Source,
            Markdown = string.IsNullOrEmpty(request.Markdown) ? string.Empty : request.Markdown,
            Activities = request.Activities.Select(a => new ActivityDefinition
            {
                ActivityName = a.ActivityName,
                AgentToolNames = a.AgentToolNames,
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
            CreatedAt = DateTime.UtcNow,
            TenantId = _tenantContext.TenantId,
            Owner = _tenantContext.LoggedInUser?? throw new InvalidOperationException("No logged in user found. Check the certificate.")
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
