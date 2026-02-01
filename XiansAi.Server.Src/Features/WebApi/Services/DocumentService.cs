using Shared.Auth;
using Shared.Utils.Services;
using Shared.Services;
using Shared.Repositories;
using Features.AgentApi.Repositories;
using Features.AgentApi.Models;
using Shared.Data.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Text.Json;

namespace Features.WebApi.Services;

public interface IDocumentService
{
    Task<ServiceResult<List<string>>> GetDocumentTypesByAgentAsync(string agentId);
    Task<ServiceResult<DocumentTypesAndActivationsResponse>> GetDocumentTypesAndActivationsByAgentAsync(string agentId);
    Task<ServiceResult<DocumentListResponse>> GetDocumentsByAgentAndTypeAsync(string agentId, string type, int skip = 0, int limit = 100, string? activationName = null);
    Task<ServiceResult<DocumentDto<JsonElement>>> GetDocumentByIdAsync(string id);
    Task<ServiceResult<DocumentDto<JsonElement>>> UpdateDocumentAsync(string id, DocumentUpdateRequest request);
    Task<ServiceResult<bool>> DeleteDocumentAsync(string id);
    Task<ServiceResult<BulkDeleteResult>> DeleteDocumentsAsync(List<string> ids);
}

/// <summary>
/// Service for managing documents through the WebAPI.
/// Provides access to document data for web UI operations.
/// Enforces tenant isolation and agent-level permissions.
/// </summary>
public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _repository;
    private readonly IAgentRepository _agentRepository;
    private readonly IPermissionsService _permissionsService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IDocumentRepository repository,
        IAgentRepository agentRepository,
        IPermissionsService permissionsService,
        ITenantContext tenantContext,
        ILogger<DocumentService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all distinct document types for a specific agent.
    /// Requires read permission on the agent.
    /// </summary>
    public async Task<ServiceResult<List<string>>> GetDocumentTypesByAgentAsync(string agentId)
    {
        try
        {
            if (string.IsNullOrEmpty(agentId))
            {
                return ServiceResult<List<string>>.BadRequest("Agent ID is required");
            }

            // Get agent to check permissions
            var agent = await _agentRepository.GetByNameInternalAsync(agentId, _tenantContext.TenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentId} not found", agentId);
                return ServiceResult<List<string>>.NotFound("Agent not found");
            }

            // Check if user has read permission
            var readPermissionResult = await _permissionsService.HasReadPermission(agent.Name);
            if (!readPermissionResult.IsSuccess)
            {
                _logger.LogWarning("Permission check failed for agent {AgentName}: {Error}", 
                    agent.Name, readPermissionResult.ErrorMessage);
                return ServiceResult<List<string>>.BadRequest(
                    readPermissionResult.ErrorMessage ?? "Failed to check permissions");
            }

            if (!readPermissionResult.Data)
            {
                _logger.LogWarning("User {UserId} attempted to access document types for agent {AgentName} without read permission",
                    _tenantContext.LoggedInUser, agent.Name);
                return ServiceResult<List<string>>.Forbidden(
                    "You do not have permission to access documents for this agent");
            }

            // Use efficient MongoDB Distinct operation at database level
            var types = await _repository.GetDistinctTypesAsync(_tenantContext.TenantId, agentId);

            return ServiceResult<List<string>>.Success(types);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document types for agent {AgentId}", agentId);
            return ServiceResult<List<string>>.InternalServerError("Error retrieving document types");
        }
    }

    /// <summary>
    /// Gets all distinct document types and activation names for a specific agent.
    /// Requires read permission on the agent.
    /// </summary>
    public async Task<ServiceResult<DocumentTypesAndActivationsResponse>> GetDocumentTypesAndActivationsByAgentAsync(string agentId)
    {
        try
        {
            if (string.IsNullOrEmpty(agentId))
            {
                return ServiceResult<DocumentTypesAndActivationsResponse>.BadRequest("Agent ID is required");
            }

            // Get agent to check permissions
            var agent = await _agentRepository.GetByNameInternalAsync(agentId, _tenantContext.TenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentId} not found", agentId);
                return ServiceResult<DocumentTypesAndActivationsResponse>.NotFound("Agent not found");
            }

            // Check if user has read permission
            var readPermissionResult = await _permissionsService.HasReadPermission(agent.Name);
            if (!readPermissionResult.IsSuccess)
            {
                _logger.LogWarning("Permission check failed for agent {AgentName}: {Error}", 
                    agent.Name, readPermissionResult.ErrorMessage);
                return ServiceResult<DocumentTypesAndActivationsResponse>.BadRequest(
                    readPermissionResult.ErrorMessage ?? "Failed to check permissions");
            }

            if (!readPermissionResult.Data)
            {
                _logger.LogWarning("User {UserId} attempted to access document types and activations for agent {AgentName} without read permission",
                    _tenantContext.LoggedInUser, agent.Name);
                return ServiceResult<DocumentTypesAndActivationsResponse>.Forbidden(
                    "You do not have permission to access documents for this agent");
            }

            // Get both distinct types and activation names in parallel for efficiency
            var typesTask = _repository.GetDistinctTypesAsync(_tenantContext.TenantId, agentId);
            var activationNamesTask = _repository.GetDistinctActivationNamesAsync(_tenantContext.TenantId, agentId);

            await Task.WhenAll(typesTask, activationNamesTask);

            var response = new DocumentTypesAndActivationsResponse
            {
                DocumentTypes = typesTask.Result,
                ActivationNames = activationNamesTask.Result
            };

            return ServiceResult<DocumentTypesAndActivationsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document types and activations for agent {AgentId}", agentId);
            return ServiceResult<DocumentTypesAndActivationsResponse>.InternalServerError("Error retrieving document types and activations");
        }
    }

    /// <summary>
    /// Gets documents by agent ID and type with pagination.
    /// Requires read permission on the agent.
    /// </summary>
    public async Task<ServiceResult<DocumentListResponse>> GetDocumentsByAgentAndTypeAsync(
        string agentId, 
        string type, 
        int skip = 0, 
        int limit = 100,
        string? activationName = null)
    {
        try
        {
            if (string.IsNullOrEmpty(agentId))
            {
                return ServiceResult<DocumentListResponse>.BadRequest("Agent ID is required");
            }

            if (string.IsNullOrEmpty(type))
            {
                return ServiceResult<DocumentListResponse>.BadRequest("Document type is required");
            }

            // Get agent to check permissions
            var agent = await _agentRepository.GetByNameInternalAsync(agentId, _tenantContext.TenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentId} not found", agentId);
                return ServiceResult<DocumentListResponse>.NotFound("Agent not found");
            }

            // Check if user has read permission
            var readPermissionResult = await _permissionsService.HasReadPermission(agent.Name);
            if (!readPermissionResult.IsSuccess)
            {
                _logger.LogWarning("Permission check failed for agent {AgentName}: {Error}", 
                    agent.Name, readPermissionResult.ErrorMessage);
                return ServiceResult<DocumentListResponse>.BadRequest(
                    readPermissionResult.ErrorMessage ?? "Failed to check permissions");
            }

            if (!readPermissionResult.Data)
            {
                _logger.LogWarning("User {UserId} attempted to access documents for agent {AgentName} without read permission",
                    _tenantContext.LoggedInUser, agent.Name);
                return ServiceResult<DocumentListResponse>.Forbidden(
                    "You do not have permission to access documents for this agent");
            }

            // Validate pagination parameters
            if (skip < 0)
            {
                skip = 0;
            }

            if (limit <= 0 || limit > 1000)
            {
                limit = 100;
            }

            // Filter at database level for efficiency
            var filter = new DocumentQueryFilter
            {
                AgentId = agentId,  // Filter by agent at DB level
                Type = type,
                ActivationName = activationName,  // Filter by activation name if provided
                Limit = limit,
                Skip = skip,
                SortBy = "CreatedAt",
                SortDescending = true
            };

            // Get total count for pagination (without skip/limit)
            var totalCount = await _repository.CountAsync(_tenantContext.TenantId, new DocumentQueryFilter
            {
                AgentId = agentId,
                Type = type,
                ActivationName = activationName
            });

            // Query documents with all filters applied at database level
            var documents = await _repository.QueryAsync(_tenantContext.TenantId, filter);

            var documentDtos = documents
                .Select(ConvertToDocumentDto)
                .ToList();

            var response = new DocumentListResponse
            {
                Documents = documentDtos,
                Total = (int)totalCount,  // Total matching documents, not just current page
                Skip = skip,
                Limit = limit
            };

            return ServiceResult<DocumentListResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving documents for agent {AgentId}, type {Type}, activation {ActivationName}", 
                agentId, type, activationName ?? "all");
            return ServiceResult<DocumentListResponse>.InternalServerError("Error retrieving documents");
        }
    }

    /// <summary>
    /// Gets a single document by its ID.
    /// Requires read permission on the associated agent.
    /// </summary>
    public async Task<ServiceResult<DocumentDto<JsonElement>>> GetDocumentByIdAsync(string id)
    {
        try
        {
            if (string.IsNullOrEmpty(id))
            {
                return ServiceResult<DocumentDto<JsonElement>>.BadRequest("Document ID is required");
            }

            var document = await _repository.GetByIdAsync(id);

            if (document == null)
            {
                return ServiceResult<DocumentDto<JsonElement>>.NotFound("Document not found");
            }

            // Check tenant access
            if (!string.IsNullOrEmpty(document.TenantId) && document.TenantId != _tenantContext.TenantId)
            {
                _logger.LogWarning("User {UserId} attempted to access document {DocumentId} from different tenant",
                    _tenantContext.LoggedInUser, id);
                return ServiceResult<DocumentDto<JsonElement>>.Forbidden("Access denied");
            }

            // Check agent permission if document has an agent
            if (!string.IsNullOrEmpty(document.AgentId))
            {
                var agent = await _agentRepository.GetByNameInternalAsync(document.AgentId, _tenantContext.TenantId);
                if (agent == null)
                {
                    _logger.LogWarning("Agent {AgentId} not found for document {DocumentId}", 
                        document.AgentId, id);
                    return ServiceResult<DocumentDto<JsonElement>>.NotFound("Associated agent not found");
                }

                // Check if user has read permission
                var readPermissionResult = await _permissionsService.HasReadPermission(agent.Name);
                if (!readPermissionResult.IsSuccess)
                {
                    _logger.LogWarning("Permission check failed for agent {AgentName}: {Error}", 
                        agent.Name, readPermissionResult.ErrorMessage);
                    return ServiceResult<DocumentDto<JsonElement>>.BadRequest(
                        readPermissionResult.ErrorMessage ?? "Failed to check permissions");
                }

                if (!readPermissionResult.Data)
                {
                    _logger.LogWarning("User {UserId} attempted to access document {DocumentId} for agent {AgentName} without read permission",
                        _tenantContext.LoggedInUser, id, agent.Name);
                    return ServiceResult<DocumentDto<JsonElement>>.Forbidden(
                        "You do not have permission to access documents for this agent");
                }
            }

            var documentDto = ConvertToDocumentDto(document);
            return ServiceResult<DocumentDto<JsonElement>>.Success(documentDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document with ID {Id}", id);
            return ServiceResult<DocumentDto<JsonElement>>.InternalServerError("Error retrieving document");
        }
    }

    /// <summary>
    /// Updates a document.
    /// Requires write permission on the associated agent.
    /// </summary>
    public async Task<ServiceResult<DocumentDto<JsonElement>>> UpdateDocumentAsync(string id, DocumentUpdateRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(id))
            {
                return ServiceResult<DocumentDto<JsonElement>>.BadRequest("Document ID is required");
            }

            // Get existing document
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                return ServiceResult<DocumentDto<JsonElement>>.NotFound("Document not found");
            }

            // Check tenant access
            if (!string.IsNullOrEmpty(existing.TenantId) && existing.TenantId != _tenantContext.TenantId)
            {
                _logger.LogWarning("User {UserId} attempted to update document {DocumentId} from different tenant",
                    _tenantContext.LoggedInUser, id);
                return ServiceResult<DocumentDto<JsonElement>>.Forbidden("Access denied");
            }

            // Check agent permission if document has an agent
            if (!string.IsNullOrEmpty(existing.AgentId))
            {
                var agent = await _agentRepository.GetByNameInternalAsync(existing.AgentId, _tenantContext.TenantId);
                if (agent == null)
                {
                    _logger.LogWarning("Agent {AgentId} not found for document {DocumentId}", 
                        existing.AgentId, id);
                    return ServiceResult<DocumentDto<JsonElement>>.NotFound("Associated agent not found");
                }

                // Check if user has write permission
                var writePermissionResult = await _permissionsService.HasWritePermission(agent.Name);
                if (!writePermissionResult.IsSuccess)
                {
                    _logger.LogWarning("Permission check failed for agent {AgentName}: {Error}", 
                        agent.Name, writePermissionResult.ErrorMessage);
                    return ServiceResult<DocumentDto<JsonElement>>.BadRequest(
                        writePermissionResult.ErrorMessage ?? "Failed to check permissions");
                }

                if (!writePermissionResult.Data)
                {
                    _logger.LogWarning("User {UserId} attempted to update document {DocumentId} for agent {AgentName} without write permission",
                        _tenantContext.LoggedInUser, id, agent.Name);
                    return ServiceResult<DocumentDto<JsonElement>>.Forbidden(
                        "You do not have permission to modify documents for this agent");
                }
            }

            // Update fields
            if (!string.IsNullOrEmpty(request.Type))
            {
                existing.Type = request.Type;
            }

            if (!string.IsNullOrEmpty(request.Key))
            {
                existing.Key = request.Key;
            }

            if (request.Content.ValueKind != JsonValueKind.Undefined && request.Content.ValueKind != JsonValueKind.Null)
            {
                existing.Content = ConvertJsonElementToBsonValue(request.Content);
            }

            if (request.Metadata != null)
            {
                var metadataJson = JsonSerializer.Serialize(request.Metadata);
                var metadataBson = BsonSerializer.Deserialize<BsonValue>(metadataJson);
                existing.Metadata = metadataBson.AsBsonDocument;
            }

            if (request.ExpiresAt.HasValue)
            {
                existing.ExpiresAt = request.ExpiresAt;
            }

            existing.UpdatedBy = _tenantContext.LoggedInUser;
            existing.UpdatedAt = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(existing);
            if (!updated)
            {
                return ServiceResult<DocumentDto<JsonElement>>.InternalServerError("Failed to update document");
            }

            var documentDto = ConvertToDocumentDto(existing);
            return ServiceResult<DocumentDto<JsonElement>>.Success(documentDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating document with ID {Id}", id);
            return ServiceResult<DocumentDto<JsonElement>>.InternalServerError("Error updating document");
        }
    }

    /// <summary>
    /// Deletes a single document.
    /// Requires write permission on the associated agent.
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteDocumentAsync(string id)
    {
        try
        {
            if (string.IsNullOrEmpty(id))
            {
                return ServiceResult<bool>.BadRequest("Document ID is required");
            }

            // Get document to check permissions
            var document = await _repository.GetByIdAsync(id);
            if (document == null)
            {
                return ServiceResult<bool>.NotFound("Document not found");
            }

            // Check tenant access
            if (!string.IsNullOrEmpty(document.TenantId) && document.TenantId != _tenantContext.TenantId)
            {
                _logger.LogWarning("User {UserId} attempted to delete document {DocumentId} from different tenant",
                    _tenantContext.LoggedInUser, id);
                return ServiceResult<bool>.Forbidden("Access denied");
            }

            // Check agent permission if document has an agent
            if (!string.IsNullOrEmpty(document.AgentId))
            {
                var agent = await _agentRepository.GetByNameInternalAsync(document.AgentId, _tenantContext.TenantId);
                if (agent == null)
                {
                    _logger.LogWarning("Agent {AgentId} not found for document {DocumentId}", 
                        document.AgentId, id);
                    return ServiceResult<bool>.NotFound("Associated agent not found");
                }

                // Check if user has write permission
                var writePermissionResult = await _permissionsService.HasWritePermission(agent.Name);
                if (!writePermissionResult.IsSuccess)
                {
                    _logger.LogWarning("Permission check failed for agent {AgentName}: {Error}", 
                        agent.Name, writePermissionResult.ErrorMessage);
                    return ServiceResult<bool>.BadRequest(
                        writePermissionResult.ErrorMessage ?? "Failed to check permissions");
                }

                if (!writePermissionResult.Data)
                {
                    _logger.LogWarning("User {UserId} attempted to delete document {DocumentId} for agent {AgentName} without write permission",
                        _tenantContext.LoggedInUser, id, agent.Name);
                    return ServiceResult<bool>.Forbidden(
                        "You do not have permission to delete documents for this agent");
                }
            }

            var deleted = await _repository.DeleteAsync(id, _tenantContext.TenantId);
            if (!deleted)
            {
                return ServiceResult<bool>.NotFound("Document not found");
            }

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document with ID {Id}", id);
            return ServiceResult<bool>.InternalServerError("Error deleting document");
        }
    }

    /// <summary>
    /// Deletes multiple documents.
    /// Requires write permission on all associated agents.
    /// </summary>
    public async Task<ServiceResult<BulkDeleteResult>> DeleteDocumentsAsync(List<string> ids)
    {
        try
        {
            if (ids == null || !ids.Any())
            {
                return ServiceResult<BulkDeleteResult>.BadRequest("At least one document ID is required");
            }

            // Get all documents to check permissions
            var documentsToDelete = new List<string>();
            var permissionDenied = new List<string>();

            foreach (var id in ids)
            {
                var document = await _repository.GetByIdAsync(id);
                if (document == null)
                {
                    _logger.LogWarning("Document {DocumentId} not found, skipping", id);
                    continue;
                }

                // Check tenant access
                if (!string.IsNullOrEmpty(document.TenantId) && document.TenantId != _tenantContext.TenantId)
                {
                    _logger.LogWarning("User {UserId} attempted to delete document {DocumentId} from different tenant",
                        _tenantContext.LoggedInUser, id);
                    permissionDenied.Add(id);
                    continue;
                }

                // Check agent permission if document has an agent
                if (!string.IsNullOrEmpty(document.AgentId))
                {
                    var agent = await _agentRepository.GetByNameInternalAsync(document.AgentId, _tenantContext.TenantId);
                    if (agent == null)
                    {
                        _logger.LogWarning("Agent {AgentId} not found for document {DocumentId}", 
                            document.AgentId, id);
                        permissionDenied.Add(id);
                        continue;
                    }

                    // Check if user has write permission
                    var writePermissionResult = await _permissionsService.HasWritePermission(agent.Name);
                    if (!writePermissionResult.IsSuccess || !writePermissionResult.Data)
                    {
                        _logger.LogWarning("User {UserId} does not have write permission for agent {AgentName} (document {DocumentId})",
                            _tenantContext.LoggedInUser, agent.Name, id);
                        permissionDenied.Add(id);
                        continue;
                    }
                }

                documentsToDelete.Add(id);
            }

            if (permissionDenied.Any())
            {
                _logger.LogWarning("User {UserId} was denied permission to delete {Count} documents",
                    _tenantContext.LoggedInUser, permissionDenied.Count);
                return ServiceResult<BulkDeleteResult>.Forbidden(
                    $"Permission denied for {permissionDenied.Count} document(s)");
            }

            var deletedCount = await _repository.DeleteManyAsync(documentsToDelete, _tenantContext.TenantId);
            
            var result = new BulkDeleteResult { DeletedCount = deletedCount };
            return ServiceResult<BulkDeleteResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting multiple documents");
            return ServiceResult<BulkDeleteResult>.InternalServerError("Error deleting documents");
        }
    }

    /// <summary>
    /// Converts a Document entity to DocumentDto.
    /// </summary>
    private DocumentDto<JsonElement> ConvertToDocumentDto(Document document)
    {
        JsonElement content = default(JsonElement);
        if (document.Content != null && !document.Content.IsBsonNull)
        {
            var contentJson = document.Content.ToJson();
            content = JsonSerializer.Deserialize<JsonElement>(contentJson);
        }

        Dictionary<string, object>? metadata = null;
        if (document.Metadata != null && !document.Metadata.IsBsonNull)
        {
            var metadataJson = document.Metadata.ToJson();
            metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);
        }

        return new DocumentDto<JsonElement>
        {
            Id = document.Id,
            Key = document.Key,
            Content = content,
            Metadata = metadata,
            AgentId = document.AgentId,
            WorkflowId = document.WorkflowId,
            ParticipantId = document.ParticipantId,
            ActivationName = document.ActivationName,
            Type = document.Type,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            ExpiresAt = document.ExpiresAt,
            CreatedBy = document.CreatedBy,
            UpdatedBy = document.UpdatedBy
        };
    }

    /// <summary>
    /// Converts a JsonElement to a BsonValue.
    /// </summary>
    private static BsonValue ConvertJsonElementToBsonValue(JsonElement element)
    {
        var json = JsonSerializer.Serialize(element);
        return BsonSerializer.Deserialize<BsonValue>(json);
    }
}

/// <summary>
/// Response model for document list operations.
/// </summary>
public class DocumentListResponse
{
    public List<DocumentDto<JsonElement>> Documents { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Limit { get; set; }
}

/// <summary>
/// Response model for document types and activation names.
/// </summary>
public class DocumentTypesAndActivationsResponse
{
    public List<string> DocumentTypes { get; set; } = new();
    public List<string> ActivationNames { get; set; } = new();
}

/// <summary>
/// Request model for updating a document.
/// </summary>
public class DocumentUpdateRequest
{
    public string? Type { get; set; }
    public string? Key { get; set; }
    public JsonElement Content { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
