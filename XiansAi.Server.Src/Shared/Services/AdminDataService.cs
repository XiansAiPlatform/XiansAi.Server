using Features.AgentApi.Repositories;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Service for admin data operations.
/// Provides access to document data for admin dashboards and analytics.
/// Tenant isolation is enforced on every read and write; identity fields are never taken from the body.
/// </summary>
public partial class AdminDataService : IAdminDataService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminDataService> _logger;

    private const int MaxDateRangeDays = 365;
    private const int MaxLimit = 1000;

    public AdminDataService(
        IDocumentRepository documentRepository,
        IAgentRepository agentRepository,
        ITenantContext tenantContext,
        ILogger<AdminDataService> logger)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AdminDataItemResponse>> GetRecordAsync(
        string tenantId,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadTenantDocumentAsync(tenantId, recordId);
        if (!loaded.IsSuccess)
        {
            return ServiceResult<AdminDataItemResponse>.Failure(
                loaded.ErrorMessage ?? "Record not found",
                loaded.StatusCode);
        }

        return ServiceResult<AdminDataItemResponse>.Success(AdminDataMapper.ToItemResponse(loaded.Data!));
    }

    public async Task<ServiceResult<AdminDataItemResponse>> CreateDataAsync(
        string tenantId,
        AdminDataCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateCreateRequest(tenantId, request);
        if (validationError != null)
        {
            return ServiceResult<AdminDataItemResponse>.BadRequest(validationError);
        }

        try
        {
            var activationName = DocumentIdentity.Normalize(request.ActivationName);
            var participantId = DocumentIdentity.Normalize(request.ParticipantId);
            var agentTask = _agentRepository.GetByNameInternalAsync(request.AgentName, tenantId);
            var existingByKeyTask = _documentRepository.GetByIdentityAsync(new DocumentIdentity(
                tenantId, request.AgentName, request.DataType, request.Key, activationName, participantId));
            await Task.WhenAll(agentTask, existingByKeyTask);

            var agent = await agentTask;
            if (agent == null)
            {
                _logger.LogWarning(
                    "Create data rejected - agent not found. TenantId: {TenantId}, AgentName: {AgentName}",
                    LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(request.AgentName));
                return ServiceResult<AdminDataItemResponse>.NotFound("Agent not found");
            }

            var existingByKey = await existingByKeyTask;
            if (existingByKey != null)
            {
                _logger.LogWarning(
                    "Create data rejected - duplicate type/key. TenantId: {TenantId}, DataType: {DataType}, Key: {Key}",
                    LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(request.DataType), LogSanitizer.Sanitize(request.Key));
                return ServiceResult<AdminDataItemResponse>.Conflict("A record with the same type and key already exists");
            }

            var actor = _tenantContext.LoggedInUser ?? "system";
            var document = new Document
            {
                TenantId = tenantId,
                AgentId = request.AgentName,
                Type = request.DataType,
                Key = request.Key,
                ActivationName = activationName,
                ParticipantId = participantId,
                WorkflowId = request.WorkflowId,
                ContentType = "JsonElement",
                Content = AdminDataMapper.ToBsonValue(request.Content),
                Metadata = AdminDataMapper.ToBsonDocument(request.Metadata),
                ExpiresAt = request.ExpiresAt,
                CreatedBy = actor,
                UpdatedBy = actor
            };

            document = await _documentRepository.CreateAsync(document);

            _logger.LogInformation(
                "Data record created - RecordId: {RecordId}, TenantId: {TenantId}, AgentName: {AgentName}, DataType: {DataType}",
                LogSanitizer.Sanitize(document.Id), LogSanitizer.Sanitize(tenantId),
                LogSanitizer.Sanitize(request.AgentName), LogSanitizer.Sanitize(request.DataType));

            return ServiceResult<AdminDataItemResponse>.Success(
                AdminDataMapper.ToItemResponse(document),
                StatusCode.Created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create data record. Error: {ErrorMessage}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<AdminDataItemResponse>.InternalServerError("Failed to create data record");
        }
    }

    public async Task<ServiceResult<AdminDataItemResponse>> UpdateDataAsync(
        string tenantId,
        string recordId,
        AdminDataUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadTenantDocumentAsync(tenantId, recordId);
        if (!loaded.IsSuccess)
        {
            return ServiceResult<AdminDataItemResponse>.Failure(
                loaded.ErrorMessage ?? "Record not found",
                loaded.StatusCode);
        }

        var existing = loaded.Data!;

        try
        {
            var newType = string.IsNullOrWhiteSpace(request.DataType) ? existing.Type : request.DataType;
            var newKey = string.IsNullOrWhiteSpace(request.Key) ? existing.Key : request.Key;
            var newActivation = DocumentIdentity.Normalize(request.ActivationName ?? existing.ActivationName);
            var newParticipant = DocumentIdentity.Normalize(request.ParticipantId ?? existing.ParticipantId);

            var identityChanged =
                !string.Equals(newType, existing.Type, StringComparison.Ordinal) ||
                !string.Equals(newKey, existing.Key, StringComparison.Ordinal) ||
                !string.Equals(newActivation, DocumentIdentity.Normalize(existing.ActivationName), StringComparison.Ordinal) ||
                !string.Equals(newParticipant, DocumentIdentity.Normalize(existing.ParticipantId), StringComparison.Ordinal);

            if (identityChanged &&
                !string.IsNullOrWhiteSpace(existing.AgentId) &&
                !string.IsNullOrWhiteSpace(newType) &&
                !string.IsNullOrWhiteSpace(newKey))
            {
                var colliding = await _documentRepository.GetByIdentityAsync(new DocumentIdentity(
                    tenantId, existing.AgentId, newType, newKey, newActivation, newParticipant));
                if (colliding != null && colliding.Id != existing.Id)
                {
                    return ServiceResult<AdminDataItemResponse>.Conflict(
                        "A record with the same type and key already exists");
                }
            }

            ApplyUpdate(existing, request);
            existing.UpdatedBy = _tenantContext.LoggedInUser ?? "system";
            existing.UpdatedAt = DateTime.UtcNow;

            var updated = await _documentRepository.UpdateAsync(existing);
            if (!updated)
            {
                return ServiceResult<AdminDataItemResponse>.InternalServerError("Failed to update data record");
            }

            _logger.LogInformation(
                "Data record updated - RecordId: {RecordId}, TenantId: {TenantId}, AgentName: {AgentName}",
                LogSanitizer.Sanitize(recordId), LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(existing.AgentId));

            return ServiceResult<AdminDataItemResponse>.Success(AdminDataMapper.ToItemResponse(existing));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update data record. RecordId: {RecordId}, Error: {ErrorMessage}",
                LogSanitizer.Sanitize(recordId), LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<AdminDataItemResponse>.InternalServerError("Failed to update data record");
        }
    }

    private static void ApplyUpdate(Document existing, AdminDataUpdateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DataType))
        {
            existing.Type = request.DataType;
        }

        if (!string.IsNullOrWhiteSpace(request.Key))
        {
            existing.Key = request.Key;
        }

        if (AdminDataMapper.HasJsonContent(request.Content))
        {
            existing.Content = AdminDataMapper.ToBsonValue(request.Content);
        }

        if (request.Metadata != null)
        {
            existing.Metadata = AdminDataMapper.ToBsonDocument(request.Metadata);
        }

        if (request.ParticipantId != null)
        {
            existing.ParticipantId = DocumentIdentity.Normalize(request.ParticipantId);
        }

        if (request.ActivationName != null)
        {
            existing.ActivationName = DocumentIdentity.Normalize(request.ActivationName);
        }

        if (request.ExpiresAt.HasValue)
        {
            existing.ExpiresAt = request.ExpiresAt;
        }
    }

    private async Task<ServiceResult<Document>> LoadTenantDocumentAsync(string tenantId, string recordId)
    {
        var tenantError = ValidateTenantId(tenantId);
        if (tenantError != null)
        {
            return ServiceResult<Document>.BadRequest(tenantError);
        }

        if (string.IsNullOrWhiteSpace(recordId))
        {
            return ServiceResult<Document>.BadRequest("RecordId is required");
        }

        try
        {
            var existing = await _documentRepository.GetByIdAsync(recordId);
            if (existing == null || existing.TenantId != tenantId)
            {
                if (existing == null)
                {
                    _logger.LogWarning("Record not found - RecordId: {RecordId}, TenantId: {TenantId}",
                        LogSanitizer.Sanitize(recordId), LogSanitizer.Sanitize(tenantId));
                }
                else
                {
                    _logger.LogWarning(
                        "Access denied - Record belongs to different tenant. RecordId: {RecordId}, RequestedTenant: {TenantId}, ActualTenant: {ActualTenantId}",
                        LogSanitizer.Sanitize(recordId), LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(existing.TenantId));
                }

                return ServiceResult<Document>.NotFound("Record not found");
            }

            return ServiceResult<Document>.Success(existing);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load record. RecordId: {RecordId}, Error: {ErrorMessage}",
                LogSanitizer.Sanitize(recordId), LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Document>.InternalServerError("Failed to load data record");
        }
    }

    private static string? ValidateCreateRequest(string tenantId, AdminDataCreateRequest request)
    {
        var tenantError = ValidateTenantId(tenantId);
        if (tenantError != null)
        {
            return tenantError;
        }

        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            return "AgentName is required";
        }

        if (string.IsNullOrWhiteSpace(request.DataType))
        {
            return "DataType is required";
        }

        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return "Key is required";
        }

        if (!AdminDataMapper.HasJsonContent(request.Content))
        {
            return "Content is required";
        }

        return null;
    }

    private static string? ValidateTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return "TenantId is required";
        }

        if (tenantId == "undefined" || tenantId == "null")
        {
            return "Invalid TenantId provided";
        }

        return null;
    }
}
