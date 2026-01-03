using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Text.Json;
using Features.AgentApi.Repositories;
using Features.AgentApi.Models;
using Shared.Data.Models;
using Shared.Utils.Services;
using Shared.Auth;
using Shared.Repositories;
using Shared.Data;

namespace Features.AgentApi.Services;

public interface IDocumentService
{
    Task<ServiceResult<JsonElement>> SaveAsync(JsonElement requestElement);
    Task<ServiceResult<JsonElement?>> GetAsync(string id);
    Task<ServiceResult<JsonElement?>> GetByKeyAsync(string type, string key);
    Task<ServiceResult<JsonElement>> QueryAsync(DocumentQueryRequest request);
    Task<ServiceResult<bool>> UpdateAsync(JsonElement documentElement);
    Task<ServiceResult<bool>> DeleteAsync(string id);
    Task<ServiceResult<BulkDeleteResult>> DeleteManyAsync(IEnumerable<string> ids);
    Task<ServiceResult<ExistsResult>> ExistsAsync(string id);
}

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<DocumentService> _logger;
    private readonly IAgentPermissionRepository _agentPermissionRepository;
    
    public DocumentService(
        IDocumentRepository repository,
        ITenantContext tenantContext,
        ILogger<DocumentService> logger,
        IAgentPermissionRepository agentPermissionRepository)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
        _agentPermissionRepository = agentPermissionRepository;
    }

    public async Task<ServiceResult<JsonElement>> SaveAsync(JsonElement requestElement)
    {
        try
        {
            // Parse the request
            DocumentRequest<JsonElement>? request;
            try
            {
                request = JsonSerializer.Deserialize<DocumentRequest<JsonElement>>(requestElement);
                if (request?.Document == null)
                {
                    return ServiceResult<JsonElement>.BadRequest("Invalid request format");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize document request");
                return ServiceResult<JsonElement>.BadRequest("Invalid request format");
            }

            var documentData = request.Document;
            var options = request.Options;

            // Validate that AgentId is provided
            if (string.IsNullOrEmpty(documentData.AgentId))
            {
                _logger.LogWarning("Document save attempted without AgentId");
                return ServiceResult<JsonElement>.BadRequest("AgentId is required for document operations");
            }

            // Check write permissions for the agent
            var hasPermission = await CheckPermissions(documentData.AgentId, PermissionLevel.Write);
            if (!hasPermission)
            {
                _logger.LogWarning("User {UserId} attempted to save document for agent {AgentId} without write permission", 
                    _tenantContext.LoggedInUser, documentData.AgentId);
                return ServiceResult<JsonElement>.Forbidden("Write access required for this agent");
            }

            // Convert to MongoDB document
            var document = new Document
            {
                TenantId = _tenantContext.TenantId,
                AgentId = documentData.AgentId,
                WorkflowId = documentData.WorkflowId,
                Type = documentData.Type,
                Key = documentData.Key,
                ContentType = "JsonElement",
                CreatedBy = _tenantContext.LoggedInUser ?? documentData.CreatedBy,
                UpdatedBy = _tenantContext.LoggedInUser ?? documentData.UpdatedBy
            };

            // Set content
            if (documentData.Content.ValueKind != JsonValueKind.Undefined && documentData.Content.ValueKind != JsonValueKind.Null)
            {
                document.Content = ConvertJsonElementToBsonValue(documentData.Content);
            }

            // Set metadata
            if (documentData.Metadata != null)
            {
                var metadataJson = JsonSerializer.Serialize(documentData.Metadata);
                var metadataBson = BsonSerializer.Deserialize<BsonValue>(metadataJson);
                document.Metadata = metadataBson.AsBsonDocument;
            }

            // Handle TTL if specified
            if (options?.TtlMinutes > 0)
            {
                document.ExpiresAt = DateTime.UtcNow.AddMinutes(options.TtlMinutes.Value);
            }
            else if (documentData.ExpiresAt.HasValue)
            {
                document.ExpiresAt = documentData.ExpiresAt;
            }

            // Handle UseKeyAsIdentifier option
            if (options?.UseKeyAsIdentifier == true)
            {
                var missingFields = new List<string>();
                if (string.IsNullOrEmpty(document.Type)) missingFields.Add("Type");
                if (string.IsNullOrEmpty(document.Key)) missingFields.Add("Key");
                
                if (missingFields.Any())
                {
                    var message = $"UseKeyAsIdentifier requires both Type and Key properties. Missing: {string.Join(", ", missingFields)}";
                    _logger.LogWarning("Document save failed: {Message}. Document Type={Type}, Key={Key}, AgentId={AgentId}", 
                        message, document.Type, document.Key, document.AgentId);
                    return ServiceResult<JsonElement>.BadRequest(message);
                }

                // Check if document with same type and key exists
                var existingByKey = await _repository.GetByKeyAsync(document.Type!, document.Key!, _tenantContext.TenantId);
                if (existingByKey != null)
                {
                    if (!(options?.Overwrite ?? false))
                    {
                        return ServiceResult<JsonElement>.Conflict("Document with same Type and Key already exists. Set Overwrite to true to update.");
                    }

                    // Update existing document
                    document.Id = existingByKey.Id;
                    document.CreatedAt = existingByKey.CreatedAt;
                    document.UpdatedAt = DateTime.UtcNow;
                    var updated = await _repository.UpdateAsync(document);
                    if (!updated)
                    {
                        return ServiceResult<JsonElement>.InternalServerError("Failed to update document");
                    }
                }
                else
                {
                    // Create new document
                    document = await _repository.CreateAsync(document);
                }
            }
            // Handle existing document by ID
            else if (!string.IsNullOrEmpty(documentData.Id))
            {
                document.Id = documentData.Id;
                
                // Check if document exists
                var exists = await _repository.ExistsAsync(document.Id, _tenantContext.TenantId);
                
                if (exists && !(options?.Overwrite ?? false))
                {
                    return ServiceResult<JsonElement>.Conflict("Document already exists. Set Overwrite to true to update.");
                }

                if (exists)
                {
                    // Update existing document
                    document.CreatedAt = documentData.CreatedAt;
                    document.UpdatedAt = DateTime.UtcNow;
                    var updated = await _repository.UpdateAsync(document);
                    if (!updated)
                    {
                        return ServiceResult<JsonElement>.InternalServerError("Failed to update document");
                    }
                }
                else
                {
                    // Create new document with specified ID
                    document = await _repository.CreateAsync(document);
                }
            }
            else
            {
                // Create new document
                document = await _repository.CreateAsync(document);
            }

            // Convert back to response format
            var responseDocument = ConvertToDocumentResponse(document);
            var responseJson = JsonSerializer.SerializeToElement(responseDocument);
            
            return ServiceResult<JsonElement>.Success(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving document");
            return ServiceResult<JsonElement>.InternalServerError("Error saving document");
        }
    }

    public async Task<ServiceResult<JsonElement?>> GetAsync(string id)
    {
        try
        {
            var document = await _repository.GetByIdAsync(id);
            
            if (document == null)
            {
                return ServiceResult<JsonElement?>.NotFound("Document not found");
            }

            // Check tenant access
            if (!string.IsNullOrEmpty(document.TenantId) && document.TenantId != _tenantContext.TenantId)
            {
                return ServiceResult<JsonElement?>.Forbidden("Access denied");
            }

            // Validate that AgentId is present
            if (string.IsNullOrEmpty(document.AgentId))
            {
                _logger.LogWarning("Document {Id} has no AgentId, denying access", id);
                return ServiceResult<JsonElement?>.Forbidden("Document has no associated agent");
            }

            // Check read permissions for the agent
            var hasPermission = await CheckPermissions(document.AgentId, PermissionLevel.Read);
            if (!hasPermission)
            {
                _logger.LogWarning("User {UserId} attempted to get document {Id} for agent {AgentId} without read permission", 
                    _tenantContext.LoggedInUser, id, document.AgentId);
                return ServiceResult<JsonElement?>.Forbidden("Read access required for this agent");
            }

            var responseDocument = ConvertToDocumentResponse(document);
            var responseJson = JsonSerializer.SerializeToElement(responseDocument);
            
            return ServiceResult<JsonElement?>.Success(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document with ID: {Id}", id);
            return ServiceResult<JsonElement?>.InternalServerError("Error retrieving document");
        }
    }

    public async Task<ServiceResult<JsonElement?>> GetByKeyAsync(string type, string key)
    {
        try
        {
            var document = await _repository.GetByKeyAsync(type, key, _tenantContext.TenantId);
            
            if (document == null)
            {
                return ServiceResult<JsonElement?>.NotFound("Document not found");
            }

            // Validate that AgentId is present
            if (string.IsNullOrEmpty(document.AgentId))
            {
                _logger.LogWarning("Document with Type: {Type} and Key: {Key} has no AgentId, denying access", type, key);
                return ServiceResult<JsonElement?>.Forbidden("Document has no associated agent");
            }

            // Check read permissions for the agent
            var hasPermission = await CheckPermissions(document.AgentId, PermissionLevel.Read);
            if (!hasPermission)
            {
                _logger.LogWarning("User {UserId} attempted to get document with Type: {Type} and Key: {Key} for agent {AgentId} without read permission", 
                    _tenantContext.LoggedInUser, type, key, document.AgentId);
                return ServiceResult<JsonElement?>.Forbidden("Read access required for this agent");
            }

            var responseDocument = ConvertToDocumentResponse(document);
            var responseJson = JsonSerializer.SerializeToElement(responseDocument);
            
            return ServiceResult<JsonElement?>.Success(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document with Type: {Type} and Key: {Key}", type, key);
            return ServiceResult<JsonElement?>.InternalServerError("Error retrieving document");
        }
    }

    public async Task<ServiceResult<JsonElement>> QueryAsync(DocumentQueryRequest request)
    {
        try
        {
            // Get all agent names the user has read access to
            var authorizedAgentNames = await _agentPermissionRepository.GetAgentNamesWithPermissionAsync(PermissionLevel.Read);
            
            // If user has no access to any agents, return empty result
            if (!authorizedAgentNames.Any())
            {
                _logger.LogInformation("User {UserId} has no read access to any agents", _tenantContext.LoggedInUser);
                var emptyResult = JsonSerializer.SerializeToElement(new List<object>());
                return ServiceResult<JsonElement>.Success(emptyResult);
            }

            // If a specific AgentId is requested, verify permission and use it
            List<string> agentIdsToQuery;
            if (!string.IsNullOrEmpty(request.Query.AgentId))
            {
                // Check if user has access to the requested agent
                if (!authorizedAgentNames.Contains(request.Query.AgentId))
                {
                    _logger.LogWarning("User {UserId} attempted to query agent {AgentId} without permission", 
                        _tenantContext.LoggedInUser, request.Query.AgentId);
                    var emptyResult = JsonSerializer.SerializeToElement(new List<object>());
                    return ServiceResult<JsonElement>.Success(emptyResult);
                }
                agentIdsToQuery = new List<string> { request.Query.AgentId };
            }
            else
            {
                // No specific agent requested, use all authorized agents
                agentIdsToQuery = authorizedAgentNames;
            }

            var filter = new DocumentQueryFilter
            {
                AgentIds = agentIdsToQuery, // Filter by specific agent or all authorized
                Type = request.Query.Type,
                Key = request.Query.Key,
                MetadataFilters = request.Query.MetadataFilters,
                Limit = request.Query.Limit ?? 100,
                Skip = request.Query.Skip ?? 0,
                SortBy = request.Query.SortBy,
                SortDescending = request.Query.SortDescending,
                CreatedAfter = request.Query.CreatedAfter,
                CreatedBefore = request.Query.CreatedBefore,
                ContentType = request.ContentType
            };

            // Query documents - filtered by authorized agents at DB level
            var documents = await _repository.QueryAsync(_tenantContext.TenantId, filter);
            
            var responseDocuments = documents.Select(ConvertToDocumentResponse).ToList();
            var responseJson = JsonSerializer.SerializeToElement(responseDocuments);
            
            return ServiceResult<JsonElement>.Success(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents");
            return ServiceResult<JsonElement>.InternalServerError("Error querying documents");
        }
    }

    public async Task<ServiceResult<bool>> UpdateAsync(JsonElement documentElement)
    {
        try
        {
            DocumentDto<JsonElement>? documentData;
            try
            {
                documentData = JsonSerializer.Deserialize<DocumentDto<JsonElement>>(documentElement);
                if (documentData == null || string.IsNullOrEmpty(documentData.Id))
                {
                    return ServiceResult<bool>.BadRequest("Invalid document format or missing ID");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize document");
                return ServiceResult<bool>.BadRequest("Invalid document format");
            }

            // Get existing document to preserve creation data
            var existing = await _repository.GetByIdAsync(documentData.Id);
            if (existing == null)
            {
                return ServiceResult<bool>.NotFound("Document not found");
            }

            // Check tenant access
            if (!string.IsNullOrEmpty(existing.TenantId) && existing.TenantId != _tenantContext.TenantId)
            {
                return ServiceResult<bool>.Forbidden("Access denied");
            }

            // Validate that AgentId is present
            if (string.IsNullOrEmpty(existing.AgentId))
            {
                _logger.LogWarning("Document {Id} has no AgentId, denying update", documentData.Id);
                return ServiceResult<bool>.Forbidden("Document has no associated agent");
            }

            // Check write permissions for the agent
            var hasPermission = await CheckPermissions(existing.AgentId, PermissionLevel.Write);
            if (!hasPermission)
            {
                _logger.LogWarning("User {UserId} attempted to update document {Id} for agent {AgentId} without write permission", 
                    _tenantContext.LoggedInUser, documentData.Id, existing.AgentId);
                return ServiceResult<bool>.Forbidden("Write access required for this agent");
            }

            // Update document
            existing.Type = documentData.Type ?? existing.Type;
            existing.UpdatedBy = _tenantContext.LoggedInUser ?? documentData.UpdatedBy;
            existing.UpdatedAt = documentData.UpdatedAt ?? DateTime.UtcNow;

            // Update content
            if (documentData.Content.ValueKind != JsonValueKind.Undefined && documentData.Content.ValueKind != JsonValueKind.Null)
            {
                existing.Content = ConvertJsonElementToBsonValue(documentData.Content);
            }

            // Update metadata
            if (documentData.Metadata != null)
            {
                var metadataJson = JsonSerializer.Serialize(documentData.Metadata);
                var metadataBson = BsonSerializer.Deserialize<BsonValue>(metadataJson);
                existing.Metadata = metadataBson.AsBsonDocument;
            }

            // Update expiration if provided
            if (documentData.ExpiresAt.HasValue)
            {
                existing.ExpiresAt = documentData.ExpiresAt;
            }

            var updated = await _repository.UpdateAsync(existing);
            
            if (!updated)
            {
                return ServiceResult<bool>.BadRequest("Failed to update document");
            }

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating document");
            return ServiceResult<bool>.BadRequest("Error updating document");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string id)
    {
        try
        {
            // First get the document to check permissions
            var document = await _repository.GetByIdAsync(id);
            if (document == null)
            {
                return ServiceResult<bool>.NotFound("Document not found");
            }

            // Check tenant access
            if (!string.IsNullOrEmpty(document.TenantId) && document.TenantId != _tenantContext.TenantId)
            {
                return ServiceResult<bool>.Forbidden("Access denied");
            }

            // Validate that AgentId is present
            if (string.IsNullOrEmpty(document.AgentId))
            {
                _logger.LogWarning("Document {Id} has no AgentId, denying delete", id);
                return ServiceResult<bool>.Forbidden("Document has no associated agent");
            }

            // Check write permissions for the agent
            var hasPermission = await CheckPermissions(document.AgentId, PermissionLevel.Write);
            if (!hasPermission)
            {
                _logger.LogWarning("User {UserId} attempted to delete document {Id} for agent {AgentId} without write permission", 
                    _tenantContext.LoggedInUser, id, document.AgentId);
                return ServiceResult<bool>.Forbidden("Write access required for this agent");
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
            _logger.LogError(ex, "Error deleting document with ID: {Id}", id);
            return ServiceResult<bool>.BadRequest("Error deleting document");
        }
    }

    public async Task<ServiceResult<BulkDeleteResult>> DeleteManyAsync(IEnumerable<string> ids)
    {
        try
        {
            var idList = ids.ToList();
            
            if (!idList.Any())
            {
                var emptyResult = new BulkDeleteResult { DeletedCount = 0 };
                return ServiceResult<BulkDeleteResult>.Success(emptyResult);
            }

            // Get all agent names the user has write access to (filtered at DB level)
            var authorizedAgentNames = await _agentPermissionRepository.GetAgentNamesWithPermissionAsync(PermissionLevel.Write);
            
            // If user has no write access to any agents, return 0 deleted
            if (!authorizedAgentNames.Any())
            {
                _logger.LogWarning("User {UserId} has no write access to any agents, cannot delete documents", _tenantContext.LoggedInUser);
                var zeroResult = new BulkDeleteResult { DeletedCount = 0 };
                return ServiceResult<BulkDeleteResult>.Success(zeroResult);
            }

            // Delete documents - filtered by both IDs and authorized agent names at DB level
            var deletedCount = await _repository.DeleteManyByIdsAndAgentsAsync(idList, authorizedAgentNames, _tenantContext.TenantId);
            
            var result = new BulkDeleteResult { DeletedCount = deletedCount };
            return ServiceResult<BulkDeleteResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting multiple documents");
            return ServiceResult<BulkDeleteResult>.InternalServerError("Error deleting documents");
        }
    }

    public async Task<ServiceResult<ExistsResult>> ExistsAsync(string id)
    {
        try
        {
            // Get the document to check permissions
            var document = await _repository.GetByIdAsync(id);
            
            // If document doesn't exist or belongs to different tenant, return false
            if (document == null || 
                (!string.IsNullOrEmpty(document.TenantId) && document.TenantId != _tenantContext.TenantId))
            {
                var result = new ExistsResult { Exists = false };
                return ServiceResult<ExistsResult>.Success(result);
            }

            // If document has no AgentId, deny access
            if (string.IsNullOrEmpty(document.AgentId))
            {
                _logger.LogWarning("Document {Id} has no AgentId, denying existence check", id);
                return ServiceResult<ExistsResult>.Forbidden("Document has no associated agent");
            }

            // Check read permissions for the agent
            var hasPermission = await CheckPermissions(document.AgentId, PermissionLevel.Read);
            if (!hasPermission)
            {
                _logger.LogWarning("User {UserId} attempted to check existence of document {Id} for agent {AgentId} without read permission", 
                    _tenantContext.LoggedInUser, id, document.AgentId);
                // Return false for documents the user doesn't have access to (don't reveal existence)
                var result = new ExistsResult { Exists = false };
                return ServiceResult<ExistsResult>.Success(result);
            }
            
            var existsResult = new ExistsResult { Exists = true };
            return ServiceResult<ExistsResult>.Success(existsResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking document existence with ID: {Id}", id);
            return ServiceResult<ExistsResult>.InternalServerError("Error checking document existence");
        }
    }

    private DocumentDto<JsonElement> ConvertToDocumentResponse(Document document)
    {
        // Convert BsonValue to JsonElement
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
            Type = document.Type,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            ExpiresAt = document.ExpiresAt,
            CreatedBy = document.CreatedBy,
            UpdatedBy = document.UpdatedBy
        };
    }

    /// <summary>
    /// Converts a JsonElement to a BsonValue, handling all JSON value types.
    /// </summary>
    private static BsonValue ConvertJsonElementToBsonValue(JsonElement element)
    {
        var json = JsonSerializer.Serialize(element);
        return BsonSerializer.Deserialize<BsonValue>(json);
    }

    /// <summary>
    /// Checks if the current user has system-level access (SysAdmin or TenantAdmin for the agent's tenant)
    /// </summary>
    private async Task<bool> HasSystemAccess(string agentId)
    {
        // System admin has access to everything
        if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            return true;

        // Tenant admin has access to everything in their tenant
        if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
        {
            var agentTenantId = await _agentPermissionRepository.GetAgentTenantAsync(agentId);
            return !string.IsNullOrEmpty(agentTenantId) &&
                   _tenantContext.TenantId.Equals(agentTenantId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Checks if the current user has the required permission level for the specified agent
    /// </summary>
    private async Task<bool> CheckPermissions(string agentId, PermissionLevel requiredLevel)
    {
        if (await HasSystemAccess(agentId))
        {
            return true;
        }

        var currentPermissions = await _agentPermissionRepository.GetAgentPermissionsAsync(agentId);
        if (currentPermissions == null)
        {
            _logger.LogWarning("No permissions found for agent: {AgentId}", agentId);
            return false;
        }

        return currentPermissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, requiredLevel);
    }
}


