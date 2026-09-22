using System.ComponentModel;
using System.Text.Json;
using Features.AgentApi.Models;
using Features.AgentApi.Repositories;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Utils;
using DocumentService = Features.AgentApi.Services.IDocumentService;

namespace Features.Mcp.Tools;

[McpServerToolType]
public sealed class DataTools(
    ITenantContext tenantContext,
    IPermissionsService permissions,
    IAgentRepository agents,
    IActivationRepository activations,
    IAdminDataService data,
    IDocumentRepository documents,
    DocumentService documentService,
    ILogger<DataTools> logger)
{
    private async Task AuthorizeAsync(McpTarget target, bool write)
    {
        McpTarget.Validate(target);
        if (target.TenantId != tenantContext.TenantId) throw new McpException("Tenant access denied.");
        var permission = write
            ? await permissions.HasWritePermission(target.AgentName)
            : await permissions.HasReadPermission(target.AgentName);
        if (!permission.IsSuccess || !permission.Data) throw new McpException("Agent access denied.");
        var agentTask = agents.GetByNameAsync(target.AgentName, tenantContext.TenantId,
            tenantContext.LoggedInUser, tenantContext.UserRoles);
        var activationTask = activations.GetByNameAndAgentAsync(tenantContext.TenantId,
            target.AgentName, target.ActivationName);
        await Task.WhenAll(agentTask, activationTask);
        var agent = await agentTask;
        var activation = await activationTask;
        if (agent is null || activation is null) throw new McpException("Agent or activation not found.");
    }

    private static T Result<T>(ServiceResult<T> result)
    {
        if ((int)result.StatusCode >= 500) throw new McpException("Data operation failed.");
        if (!result.IsSuccess) throw new McpException(result.ErrorMessage ?? "Data operation failed.");
        return result.Data!;
    }

    [McpServerTool(Name = "list_data_types", ReadOnly = true)]
    [Description("Discover saved data types in this activation. These are record categories, not JSON schemas.")]
    public async Task<List<string>> ListDataTypes(McpTarget target)
    {
        await AuthorizeAsync(target, false);
        return await documents.GetDistinctTypesAsync(tenantContext.TenantId, target.AgentName, target.ActivationName);
    }

    [McpServerTool(Name = "list_data_records", ReadOnly = true)]
    [Description("List saved records of a data type in this activation, newest first. Dates must include a UTC offset; range at most 365 days. Skip is zero-based; limit at most 100.")]
    public async Task<AdminDataListResponse> ListDataRecords(McpTarget target, string dataType, DateTimeOffset startDate,
        DateTimeOffset endDate, int skip = 0, int limit = 20)
    {
        await AuthorizeAsync(target, false);
        if (string.IsNullOrWhiteSpace(dataType)) throw new McpException("Data type is required.");
        if (skip < 0) throw new McpException("Skip must be non-negative.");
        if (limit < 1) throw new McpException("Limit must be positive.");
        if (limit > 100) throw new McpException("Limit must not exceed 100.");
        return Result(await data.GetDataAsync(new AdminDataListRequest
        {
            TenantId = tenantContext.TenantId, AgentName = target.AgentName, ActivationName = target.ActivationName,
            DataType = dataType, StartDate = startDate.UtcDateTime, EndDate = endDate.UtcDateTime, Skip = skip, Limit = limit
        }));
    }

    [McpServerTool(Name = "save_data_record")]
    [Description("Create a new record in this activation's Data Explorer. Content must be a string containing a JSON object, for example {\"title\":\"Report\"}. Does not overwrite existing records. ParticipantId is optional attribution, not an access boundary.")]
    public async Task<JsonElement> SaveDataRecord(McpTarget target, string dataType, string content, string? key = null,
        string? participantId = null)
    {
        await AuthorizeAsync(target, true);
        if (string.IsNullOrWhiteSpace(dataType)) throw new McpException("Data type is required.");
        var parsedContent = ParseContent(content);
        return Result(await documentService.SaveAsync(new DocumentRequest<JsonElement>
        {
            Document = new DocumentDto<JsonElement>
            {
                AgentId = target.AgentName, ActivationName = target.ActivationName, Type = dataType,
                Content = parsedContent, Key = key, ParticipantId = participantId
            }
        }));
    }

    private static JsonElement ParseContent(string content)
    {
        JsonElement parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<JsonElement>(content);
        }
        catch (JsonException)
        {
            throw new McpException("Content must be valid JSON object text.");
        }
        if (parsed.ValueKind != JsonValueKind.Object) throw new McpException("Content must be a JSON object.");
        return parsed;
    }

    [McpServerTool(Name = "delete_data_record", Destructive = true)]
    [Description("Permanently delete one record by exact ID from list_data_records. Set confirmed=true only after the user explicitly approves deletion.")]
    public async Task<AdminDataDeleteRecordResponse> DeleteDataRecord(McpTarget target, string recordId, bool confirmed = false)
    {
        await AuthorizeAsync(target, true);
        RequireConfirmation(confirmed);
        if (!MongoDB.Bson.ObjectId.TryParse(recordId, out _)) throw new McpException("Invalid record ID.");
        return Result(await data.DeleteRecordAsync(new AdminDataDeleteRecordRequest
            { TenantId = tenantContext.TenantId, RecordId = recordId,
                AgentName = target.AgentName, ActivationName = target.ActivationName }));
    }

    [McpServerTool(Name = "delete_data_records", Destructive = true)]
    [Description("Permanently delete at most 100 records of a data type in this activation within a date range (at most 365 days). Requests matching more than 100 records are rejected; narrow the date range. List records first and obtain explicit user approval before setting confirmed=true.")]
    public async Task<AdminDataDeleteResponse> DeleteDataRecords(McpTarget target, string dataType, DateTimeOffset startDate,
        DateTimeOffset endDate, bool confirmed = false)
    {
        var completed = false;
        var deletedCount = 0;
        try
        {
            if (target is null) throw new McpException("Target is required.");
            await AuthorizeAsync(target, true);
            RequireConfirmation(confirmed);
            if (string.IsNullOrWhiteSpace(dataType)) throw new McpException("Data type is required.");
            var preview = Result(await data.GetDataAsync(new AdminDataListRequest
            {
                TenantId = tenantContext.TenantId, AgentName = target.AgentName, ActivationName = target.ActivationName,
                DataType = dataType, StartDate = startDate.UtcDateTime, EndDate = endDate.UtcDateTime, Limit = 100
            }));
            if (preview.Total > 100 || preview.Data.Count > 100)
                throw new McpException("Bulk deletion is limited to 100 records. Narrow the date range.");
            var result = Result(await data.DeleteDataAsync(new AdminDataDeleteRequest
            {
                TenantId = tenantContext.TenantId, AgentName = target.AgentName, ActivationName = target.ActivationName,
                DataType = dataType, StartDate = startDate.UtcDateTime, EndDate = endDate.UtcDateTime,
                RecordIds = preview.Data.Select(record => record.Id).ToList()
            }));
            completed = true;
            deletedCount = result.DeletedCount;
            return result;
        }
        finally
        {
            logger.LogInformation("MCP bulk-delete audit: User={User}, Tenant={Tenant}, Agent={Agent}, Activation={Activation}, Type={Type}, Start={Start}, End={End}, Completed={Completed}, DeletedCount={DeletedCount}",
                LogSanitizer.Sanitize(tenantContext.LoggedInUser), LogSanitizer.Sanitize(tenantContext.TenantId),
                LogSanitizer.Sanitize(target?.AgentName), LogSanitizer.Sanitize(target?.ActivationName),
                LogSanitizer.Sanitize(dataType), startDate, endDate, completed, deletedCount);
        }
    }

    private static void RequireConfirmation(bool confirmed)
    {
        if (!confirmed) throw new McpException("Explicit user confirmation is required before permanent deletion.");
    }
}
