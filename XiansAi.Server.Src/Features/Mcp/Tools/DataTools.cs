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
using DocumentService = Features.AgentApi.Services.IDocumentService;

namespace Features.Mcp.Tools;

[McpServerToolType]
public sealed class DataTools(
    IHttpContextAccessor httpContextAccessor,
    ITenantContext tenantContext,
    IPermissionsService permissions,
    IAgentRepository agents,
    IActivationRepository activations,
    IAdminDataService data,
    IDocumentRepository documents,
    DocumentService documentService)
{
    private string Route(string key) => httpContextAccessor.HttpContext!.Request.RouteValues[key]?.ToString()
        ?? throw new McpException($"Missing {key}.");

    private async Task AuthorizeAsync(bool write)
    {
        if (Route("tenantId") != tenantContext.TenantId) throw new McpException("Tenant access denied.");
        var permission = await permissions.HasReadPermission(Route("agentName"));
        if (write) permission = await permissions.HasWritePermission(Route("agentName"));
        if (!permission.IsSuccess || !permission.Data) throw new McpException("Agent access denied.");
        var agent = await agents.GetByNameAsync(Route("agentName"), tenantContext.TenantId,
            tenantContext.LoggedInUser, tenantContext.UserRoles);
        var activation = await activations.GetByNameAndAgentAsync(tenantContext.TenantId,
            Route("agentName"), Route("activationName"));
        if (agent is null || activation is null) throw new McpException("Agent or activation not found.");
    }

    private static T Result<T>(ServiceResult<T> result)
    {
        if (!result.IsSuccess) throw new McpException(result.ErrorMessage ?? "Data operation failed.");
        return result.Data!;
    }

    [McpServerTool(Name = "list_data_types", ReadOnly = true)]
    [Description("Discover saved data types in this activation. These are record categories, not JSON schemas.")]
    public async Task<List<string>> ListDataTypes()
    {
        await AuthorizeAsync(false);
        return await documents.GetDistinctTypesAsync(tenantContext.TenantId, Route("agentName"), Route("activationName"));
    }

    [McpServerTool(Name = "list_data_records", ReadOnly = true)]
    [Description("List saved records of a data type in this activation, newest first. Dates must include a UTC offset; range at most 365 days. Skip is zero-based; limit at most 100.")]
    public async Task<AdminDataListResponse> ListDataRecords(string dataType, DateTimeOffset startDate,
        DateTimeOffset endDate, int skip = 0, int limit = 20)
    {
        await AuthorizeAsync(false);
        if (limit > 100) throw new McpException("Limit must not exceed 100.");
        return Result(await data.GetDataAsync(new AdminDataListRequest
        {
            TenantId = tenantContext.TenantId, AgentName = Route("agentName"), ActivationName = Route("activationName"),
            DataType = dataType, StartDate = startDate.UtcDateTime, EndDate = endDate.UtcDateTime, Skip = skip, Limit = limit
        }));
    }

    [McpServerTool(Name = "save_data_record")]
    [Description("Create a new JSON object record in this activation's Data Explorer. Does not overwrite existing records. ParticipantId is optional attribution, not an access boundary.")]
    public async Task<JsonElement> SaveDataRecord(string dataType, JsonElement content, string? key = null,
        string? participantId = null)
    {
        await AuthorizeAsync(true);
        if (string.IsNullOrWhiteSpace(dataType)) throw new McpException("Data type is required.");
        if (content.ValueKind != JsonValueKind.Object) throw new McpException("Content must be a JSON object.");
        return Result(await documentService.SaveAsync(new DocumentRequest<JsonElement>
        {
            Document = new DocumentDto<JsonElement>
            {
                AgentId = Route("agentName"), ActivationName = Route("activationName"), Type = dataType,
                Content = content, Key = key, ParticipantId = participantId
            }
        }));
    }

    [McpServerTool(Name = "delete_data_record", Destructive = true)]
    [Description("Permanently delete one record by exact ID from list_data_records. Set confirmed=true only after the user explicitly approves deletion.")]
    public async Task<AdminDataDeleteRecordResponse> DeleteDataRecord(string recordId, bool confirmed = false)
    {
        await AuthorizeAsync(true);
        RequireConfirmation(confirmed);
        if (!MongoDB.Bson.ObjectId.TryParse(recordId, out _)) throw new McpException("Invalid record ID.");
        var record = await documents.GetByIdAsync(recordId);
        if (record is null || record.TenantId != tenantContext.TenantId ||
            record.AgentId != Route("agentName") || record.ActivationName != Route("activationName"))
            throw new McpException("Record not found in this activation.");
        return Result(await data.DeleteRecordAsync(new AdminDataDeleteRecordRequest
            { TenantId = tenantContext.TenantId, RecordId = recordId }));
    }

    [McpServerTool(Name = "delete_data_records", Destructive = true)]
    [Description("Permanently delete a data type's records in this activation within a date range (at most 365 days). List records first and obtain explicit user approval before setting confirmed=true.")]
    public async Task<AdminDataDeleteResponse> DeleteDataRecords(string dataType, DateTimeOffset startDate,
        DateTimeOffset endDate, bool confirmed = false)
    {
        await AuthorizeAsync(true);
        RequireConfirmation(confirmed);
        return Result(await data.DeleteDataAsync(new AdminDataDeleteRequest
        {
            TenantId = tenantContext.TenantId, AgentName = Route("agentName"), ActivationName = Route("activationName"),
            DataType = dataType, StartDate = startDate.UtcDateTime, EndDate = endDate.UtcDateTime
        }));
    }

    private static void RequireConfirmation(bool confirmed)
    {
        if (!confirmed) throw new McpException("Explicit user confirmation is required before permanent deletion.");
    }
}
