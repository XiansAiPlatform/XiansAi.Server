using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using XiansAi.Server.Database.Repositories;
using MongoDB.Driver;
using XiansAi.Server.GenAi;
using XiansAi.Server.Auth;

namespace XiansAi.Server.Services.Lib;

public class FlowDefinitionRequest
{
    [Required]
    [JsonPropertyName("agentName")]
    public required string AgentName { get; set; }

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
    private readonly ITenantContext _tenantContext;
    public DefinitionsServerEndpoint(
        IDatabaseService databaseService,
        ILogger<DefinitionsServerEndpoint> logger,
        IOpenAIClientService openAIClientService,
        ITenantContext tenantContext
    )
    {
        _databaseService = databaseService;
        _logger = logger;
        _openAIClientService = openAIClientService;
        _tenantContext = tenantContext;
    }

    public async Task<IResult> CreateAsync(FlowDefinitionRequest request)
    {
        var definitionRepository = new FlowDefinitionRepository(await _databaseService.GetDatabase());

        var definition = CreateFlowDefinitionFromRequest(request);

        // IF another user has the same type name, we reject the request
        var ownedByAnother = await definitionRepository.IfExistsInAnotherOwner(definition.TypeName, definition.Owner);
        if (ownedByAnother)
        {
            _logger.LogInformation("Another user has already used this flow type name {typeName}. Rejecting request", definition.TypeName);
            return Results.BadRequest($"Another user has already used this flow type name {definition.TypeName}. Please choose a different flow name.");
        }

        
        // Find existing definition with the same hash
        var existingDefinition = await definitionRepository.GetLatestByTypeNameAndOwnerAsync(definition.TypeName, definition.Owner);

        // no definition in database
        if (existingDefinition == null)
        {
            _logger.LogInformation("No existing definition found, creating new definition");
            await GenerateMarkdown(definition);
            await definitionRepository.CreateAsync(definition);
            return Results.Ok("No existing definition found, new definition created successfully");
        }

        // no markdown in the existing record, but there is a markdown in the new definition
        if (string.IsNullOrEmpty(existingDefinition.Markdown) && !string.IsNullOrEmpty(definition.Markdown))
        {
            _logger.LogInformation("No markdown in the existing definition, generating markdown for new definition");
            await GenerateMarkdown(definition);
            await definitionRepository.CreateAsync(definition);
            return Results.Ok("No markdown in the existing definition, new definition created successfully");
        }

        if (existingDefinition.Hash != definition.Hash)
        {
            _logger.LogInformation("Definition had a different hash, generating markdown for new definition");
            await GenerateMarkdown(definition);
            await definitionRepository.CreateAsync(definition);
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
            AgentName = request.AgentName,
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
