using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;

namespace Features.AgentApi.Services.Lib;

public class FlowDefinitionRequest
{
    [JsonPropertyName("agent")]
    public string? Agent { get; set; }
    
    [Required]
    [JsonPropertyName("workflowType")]
    public required string WorkflowType { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [Required]
    [JsonPropertyName("activityDefinitions")]
    public required List<ActivityDefinitionRequest> ActivityDefinitions { get; set; }

    [Required]
    [JsonPropertyName("parameterDefinitions")]
    public required List<ParameterDefinitionRequest> ParameterDefinitions { get; set; }
    
    [JsonPropertyName("systemScoped")]
    public bool SystemScoped { get; set; } = false;

    [JsonPropertyName("activable")]
    public bool Activable { get; set; } = false;

    [JsonPropertyName("onboardingJson")]
    public string? OnboardingJson { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }
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

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;
}

public class CreateAgentRequest
{
    [Required]
    [JsonPropertyName("agentName")]
    public required string AgentName { get; set; }

    [JsonPropertyName("systemScoped")]
    public bool SystemScoped { get; set; } = false;

    [JsonPropertyName("onboardingJson")]
    public string? OnboardingJson { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }
}

public interface IDefinitionsService
{
    Task<IResult> CreateAsync(FlowDefinitionRequest request);
    Task<IResult> CheckHash(string workflowType, bool systemScoped, string hash);
    Task<IResult> CreateAgentAsync(CreateAgentRequest request);
}

public class DefinitionsService : IDefinitionsService
{
    private readonly ILogger<DefinitionsService> _logger;
    private readonly Repositories.IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMarkdownService _markdownService; 
    private readonly IAgentPermissionRepository _agentPermissionRepository;
    
    public DefinitionsService(
        Repositories.IFlowDefinitionRepository flowDefinitionRepository,
        IAgentRepository agentRepository,
        ILogger<DefinitionsService> logger,
        ITenantContext tenantContext,
        IMarkdownService markdownService,
        IAgentPermissionRepository agentPermissionRepository
    )
    {
        _flowDefinitionRepository = flowDefinitionRepository;
        _agentRepository = agentRepository;
        _logger = logger;
        _tenantContext = tenantContext;
        _markdownService = markdownService;
        _agentPermissionRepository = agentPermissionRepository;
    }

    public async Task<IResult> CreateAsync(FlowDefinitionRequest request)
    {
        if (string.IsNullOrEmpty(request.Agent))
        {
            _logger.LogWarning("Agent name is empty or null");
            return Results.Problem(
                title: "Bad Request",
                detail: "Agent name is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var currentUser = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("No logged in user found");
        
        // Only system admins can create system agents
        if (request.SystemScoped && !_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
        {
            _logger.LogError("User {UserId} attempted to create system agent {AgentName} without system admin permission", currentUser, request.Agent);
            throw new InvalidOperationException("User does not have system admin permission to create `system` agents");
        }

        // if not systemScoped and there is an systemscoped agent with the same name, throw an error
        if (!request.SystemScoped && await _agentRepository.IsSystemAgent(request.Agent))
        {
            _logger.LogError("User {UserId} attempted to create non-system agent {AgentName} with the same name as an existing system scoped agent", currentUser, request.Agent);
            return Results.Problem(
                title: "Internal Server Error",
                detail: $"An system scoped agent with the same name already exists. Trying to create definition for {request.WorkflowType} for agent {request.Agent}",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Ensure agent exists using thread-safe upsert operation
        await _agentRepository.UpsertAgentAsync(
            request.Agent, 
            request.SystemScoped, 
            _tenantContext.TenantId, 
            currentUser, 
            request.OnboardingJson,
            request.Description,
            request.Summary,
            request.Version,
            request.Author);

        // Check if the user has permissions for this agent
        var hasPermission = await CheckPermissions(request.Agent, PermissionLevel.Write);
        if (!hasPermission)
        {
            var warningMessage = @$"User `{currentUser}` does not have write permission 
                for agent `{request.Agent}` which is owned by another user. 
                Please use a different name or ask the owner to share 
                the agent with you with write permission.";
            _logger.LogWarning(warningMessage);
            return Results.Problem(
                title: "Forbidden",
                detail: warningMessage,
                statusCode: StatusCodes.Status403Forbidden);
        }
        
        // Use tenant-aware method to get existing definition
        var existingDefinition = await _flowDefinitionRepository.GetByWorkflowTypeAsync(request.WorkflowType, request.SystemScoped, _tenantContext.TenantId );
        var definition = CreateFlowDefinitionFromRequest(request, existingDefinition, request.SystemScoped);
        
        if (existingDefinition != null)
        {
            if (existingDefinition.Hash != definition.Hash)
            {
                _logger.LogInformation("Flow definition with workflow type {WorkflowType} has changed hash. Deleting existing and creating new one.", request.WorkflowType);
                await _flowDefinitionRepository.DeleteAsync(existingDefinition.Id);
                await GenerateMarkdown(definition);
                // Create new definition with fresh ID
                definition.Id = ObjectId.GenerateNewId().ToString();
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

    public async Task<IResult> CheckHash(string workflowType, bool systemScoped, string hash)
    {
        // Use tenant-aware method to check hash
        var existingDefinition = await _flowDefinitionRepository.GetByWorkflowTypeAsync(workflowType, systemScoped, _tenantContext.TenantId);
        if (existingDefinition == null)
            return Results.NotFound("No existing definition");

        if (existingDefinition.Hash == hash){
            return Results.Ok("Hash matches existing definition");
        }

        return Results.NotFound("Hash does not match");
    }

    public async Task<IResult> CreateAgentAsync(CreateAgentRequest request)
    {
        if (string.IsNullOrEmpty(request.AgentName))
        {
            _logger.LogWarning("Agent name is empty or null");
            return Results.Problem(
                title: "Bad Request",
                detail: "Agent name is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var currentUser = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("No logged in user found");
        
        // Only system admins can create system agents
        if (request.SystemScoped && !_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
        {
            _logger.LogError("User {UserId} attempted to create system agent {AgentName} without system admin permission", currentUser, request.AgentName);
            return Results.Problem(
                title: "Forbidden",
                detail: "User does not have system admin permission to create `system` agents",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // if not systemScoped and there is a systemscoped agent with the same name, throw an error
        if (!request.SystemScoped && await _agentRepository.IsSystemAgent(request.AgentName))
        {
            _logger.LogError("User {UserId} attempted to create non-system agent {AgentName} with the same name as an existing system scoped agent", currentUser, request.AgentName);
            return Results.Problem(
                title: "Conflict",
                detail: $"A system scoped agent with the same name already exists. Agent name: {request.AgentName}",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Create or update agent using thread-safe upsert operation
        var agent = await _agentRepository.UpsertAgentAsync(
            request.AgentName, 
            request.SystemScoped, 
            _tenantContext.TenantId, 
            currentUser, 
            request.OnboardingJson,
            request.Description,
            request.Summary,
            request.Version,
            request.Author);
        
        _logger.LogInformation("Agent {AgentName} created/updated successfully by user {UserId}", request.AgentName, currentUser);
        
        return Results.Ok(new 
        { 
            message = "Agent created successfully",
            agentName = agent.Name,
            systemScoped = agent.SystemScoped,
            createdBy = agent.CreatedBy,
            description = agent.Description,
            summary = agent.Summary,
            version = agent.Version,
            author = agent.Author
        });
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

    private FlowDefinition CreateFlowDefinitionFromRequest(FlowDefinitionRequest request, FlowDefinition? existingDefinition = null, bool systemScoped = false)
    {
        var userId = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("No logged in user found. Check the certificate.");

        return new FlowDefinition
        {
            Id = existingDefinition?.Id ?? ObjectId.GenerateNewId().ToString(),
            WorkflowType = request.WorkflowType,
            Agent = request.Agent ?? request.WorkflowType,
            Name = request.Name,
            Hash = ComputeHash(JsonSerializer.Serialize(request)),
            Source = string.IsNullOrEmpty(request.Source) ? string.Empty : request.Source,
            Markdown = string.IsNullOrEmpty(existingDefinition?.Markdown) ? string.Empty : existingDefinition.Markdown,
            SystemScoped = systemScoped,
            Activable = request.Activable,
            Tenant = systemScoped ? null : _tenantContext.TenantId,
            Summary = request.Summary,
            OnboardingJson = request.OnboardingJson,
            ActivityDefinitions = request.ActivityDefinitions.Select(a => new ActivityDefinition
            {
                ActivityName = a.ActivityName,
                AgentToolNames = a.AgentToolNames,
                KnowledgeIds = a.KnowledgeIds,
                ParameterDefinitions = a.ParameterDefinitions.Select(p => new ParameterDefinition
                {
                    Name = p.Name,
                    Type = p.Type,
                    Description = p.Description,
                    Optional = p.Optional
                }).ToList()
            }).ToList(),
            ParameterDefinitions = request.ParameterDefinitions.Select(p => new ParameterDefinition
            {
                Name = p.Name,
                Type = p.Type,
                Description = p.Description,
                Optional = p.Optional
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

    private async Task<bool> HasSystemAccess(string agentName)
    {
        // System admin has access to everything
        if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            return true;

        // Tenant admin has access to everything in their tenant
        if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
        {
            var agentTenantId = await _agentPermissionRepository.GetAgentTenantAsync(agentName);
            return !string.IsNullOrEmpty(agentTenantId) &&
                   _tenantContext.TenantId.Equals(agentTenantId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task<bool> CheckPermissions(string agentName, PermissionLevel requiredLevel)
    {
        if (await HasSystemAccess(agentName))
        {
            return true;
        }

        var currentPermissions = await _agentPermissionRepository.GetAgentPermissionsAsync(agentName);
        _logger.LogInformation("Permissions: {Permissions}", currentPermissions);

        return currentPermissions?.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, requiredLevel) ?? false;
    }
}
