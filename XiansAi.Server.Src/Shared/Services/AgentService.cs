using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;
using Features.WebApi.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Shared.Services;

public class AgentDeletionResult
{
    public string Message { get; set; } = string.Empty;
    public int DeletedFlowDefinitions { get; set; }
    public int DeletedKnowledgeItems { get; set; }
    public int DeletedSchedules { get; set; }
    public int DeletedDocuments { get; set; }
    public int DeletedLogs { get; set; }
    public int DeletedUsageEvents { get; set; }
    public int RevokedApiKeys { get; set; }
    public bool AgentDeleted { get; set; }
}

public interface IAgentDeletionService
{
    Task<ServiceResult<AgentDeletionResult>> DeleteAgentAsync(string agentName, bool systemScoped);
}

/// <summary>
/// Shared agent service that performs cascading deletes across agent resources.
/// </summary>
public class AgentDeletionService : IAgentDeletionService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly IUsageEventRepository _usageEventRepository;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IPermissionsService _permissionsService;
    private readonly ITenantContext _tenantContext;
    private readonly IScheduleService _scheduleService;
    private readonly IDatabaseService _databaseService;
    private readonly IActivationRepository _activationRepository;
    private readonly ILogger<AgentService> _logger;

    private IMongoCollection<BsonDocument> LogsCollection => _databaseService.GetDatabaseAsync().Result.GetCollection<BsonDocument>("logs");
    private IMongoCollection<BsonDocument> DocumentsCollection => _databaseService.GetDatabaseAsync().Result.GetCollection<BsonDocument>("documents");

    public AgentDeletionService(
        IAgentRepository agentRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        IKnowledgeRepository knowledgeRepository,
        IUsageEventRepository usageEventRepository,
        IApiKeyRepository apiKeyRepository,
        IPermissionsService permissionsService,
        ITenantContext tenantContext,
        IScheduleService scheduleService,
        IDatabaseService databaseService,
        IActivationRepository activationRepository,
        ILogger<AgentService> logger)
    {
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
        _knowledgeRepository = knowledgeRepository ?? throw new ArgumentNullException(nameof(knowledgeRepository));
        _usageEventRepository = usageEventRepository ?? throw new ArgumentNullException(nameof(usageEventRepository));
        _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _activationRepository = activationRepository ?? throw new ArgumentNullException(nameof(activationRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AgentDeletionResult>> DeleteAgentAsync(string agentName, bool systemScoped)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return ServiceResult<AgentDeletionResult>.BadRequest("Agent name is required");
            }

            var sanitizedAgentName = Agent.SanitizeAndValidateName(agentName);
            var tenantId = systemScoped ? null : _tenantContext.TenantId;

            // Permission check – system scoped agents require sysadmin, tenant scoped require owner
            if (systemScoped)
            {
                if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                {
                    return ServiceResult<AgentDeletionResult>.Forbidden("Only system administrators can delete system agents");
                }
            }
            else
            {
                var ownerPermission = await _permissionsService.HasOwnerPermission(sanitizedAgentName);
                if (!ownerPermission.IsSuccess)
                {
                    return ServiceResult<AgentDeletionResult>.BadRequest(ownerPermission.ErrorMessage ?? "Failed to check permissions");
                }
                if (!ownerPermission.Data)
                {
                    return ServiceResult<AgentDeletionResult>.Forbidden("You must have owner permission to delete this agent");
                }
            }

            var agent = await _agentRepository.GetByNameInternalAsync(sanitizedAgentName, tenantId);
            if (agent == null)
            {
                return ServiceResult<AgentDeletionResult>.NotFound("Agent not found");
            }

            // Check if there are any activations for this agent
            if (tenantId != null)
            {
                var activations = await _activationRepository.GetByAgentNameAsync(sanitizedAgentName, tenantId);
                if (activations != null && activations.Count > 0)
                {
                    _logger.LogWarning("Cannot delete agent {AgentName} - {Count} activation(s) exist", sanitizedAgentName, activations.Count);
                    return ServiceResult<AgentDeletionResult>.Conflict(
                        $"Cannot delete agent '{sanitizedAgentName}' because it has {activations.Count} activation(s). Please delete all activations first.");
                }
            }

            var result = new AgentDeletionResult();

            // Delete schedules (tenant scoped only)
            if (!systemScoped)
            {
                var scheduleResult = await _scheduleService.DeleteAllSchedulesByAgentAsync(sanitizedAgentName);
                if (scheduleResult.IsSuccess)
                {
                    result.DeletedSchedules = scheduleResult.Data?.DeletedCount ?? 0;
                }
                else
                {
                    _logger.LogWarning("Failed deleting schedules for agent {Agent}: {Error}", sanitizedAgentName, scheduleResult.ErrorMessage);
                }
            }

            // Delete knowledge (all versions)
            try
            {
                var knowledgeItems = systemScoped
                    ? await _knowledgeRepository.GetSystemScopedByAgentAsync<Knowledge>(sanitizedAgentName)
                    : await _knowledgeRepository.GetUniqueLatestAsync<Knowledge>(tenantId, new List<string> { sanitizedAgentName });

                foreach (var knowledge in knowledgeItems)
                {
                    var deleteTenant = knowledge.SystemScoped ? null : tenantId;
                    var knowledgeDeleted = await _knowledgeRepository.DeleteAllVersionsAsync<Knowledge>(knowledge.Name, knowledge.Agent, deleteTenant);
                    if (knowledgeDeleted)
                    {
                        result.DeletedKnowledgeItems++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete knowledge for agent {Agent}", sanitizedAgentName);
            }

            // Delete flow definitions
            result.DeletedFlowDefinitions = (int)await _flowDefinitionRepository.DeleteByAgentAsync(sanitizedAgentName, tenantId);

            // Delete documents tied to agent id
            result.DeletedDocuments = await DeleteDocumentsByAgentAsync(agent.Id, tenantId);

            // Delete logs for agent
            result.DeletedLogs = await DeleteLogsByAgentAsync(sanitizedAgentName, tenantId);

            // Delete usage events referencing agent in workflow_id
            result.DeletedUsageEvents = await _usageEventRepository.DeleteByAgentAsync(tenantId ?? string.Empty, sanitizedAgentName);

            // Revoke api keys that appear to belong to this agent (name match)
            result.RevokedApiKeys = await RevokeApiKeysByNameMatchAsync(tenantId, sanitizedAgentName);

            // Finally delete the agent record (also deletes definitions inside repository)
            var agentDeleted = await _agentRepository.DeleteAsync(agent.Id, _tenantContext.LoggedInUser, _tenantContext.UserRoles);
            result.AgentDeleted = agentDeleted;
            if (!agentDeleted)
            {
                return ServiceResult<AgentDeletionResult>.BadRequest("Failed to delete the agent");
            }

            result.Message = "Agent and associated resources deleted";
            return ServiceResult<AgentDeletionResult>.Success(result);
        }
        catch (ValidationException ex)
        {
            return ServiceResult<AgentDeletionResult>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting agent {AgentName}", agentName);
            return ServiceResult<AgentDeletionResult>.InternalServerError("An error occurred while deleting the agent");
        }
    }

    private async Task<int> DeleteDocumentsByAgentAsync(string agentId, string? tenantId)
    {
        var filters = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Eq("agent_id", agentId)
        };

        filters.Add(string.IsNullOrEmpty(tenantId)
            ? Builders<BsonDocument>.Filter.Eq("tenant_id", BsonNull.Value)
            : Builders<BsonDocument>.Filter.Eq("tenant_id", tenantId));

        var filter = Builders<BsonDocument>.Filter.And(filters);
        var result = await DocumentsCollection.DeleteManyAsync(filter);
        return (int)result.DeletedCount;
    }

    private async Task<int> DeleteLogsByAgentAsync(string agentName, string? tenantId)
    {
        var filters = new List<FilterDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Filter.Eq("agent", agentName)
        };

        filters.Add(string.IsNullOrEmpty(tenantId)
            ? Builders<BsonDocument>.Filter.Eq("tenant_id", BsonNull.Value)
            : Builders<BsonDocument>.Filter.Eq("tenant_id", tenantId));

        var filter = Builders<BsonDocument>.Filter.And(filters);
        var result = await LogsCollection.DeleteManyAsync(filter);
        return (int)result.DeletedCount;
    }

    private async Task<int> RevokeApiKeysByNameMatchAsync(string? tenantId, string agentName)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            return 0;
        }

        var keys = await _apiKeyRepository.GetByTenantAsync(tenantId, hasRevoked: true);
        var matching = keys.Where(k => k.Name.Contains(agentName, StringComparison.OrdinalIgnoreCase) && k.RevokedAt == null).ToList();

        var revoked = 0;
        foreach (var key in matching)
        {
            var ok = await _apiKeyRepository.RevokeAsync(key.Id, tenantId);
            if (ok) revoked++;
        }

        return revoked;
    }
}
