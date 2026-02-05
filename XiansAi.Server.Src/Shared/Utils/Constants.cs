using Temporalio.Api.Enums.V1;

namespace Shared.Utils;

public static class Constants {
    public const string CurrentOwnerKey = "current";
    public const string TenantIdKey = "tenantId";
    public const string UserIdKey = "userId";
    public const string AgentKey = "agent";
    public const string SystemScopedKey = "systemScoped";
    public const string IdPostfixKey = "idPostfix";
    public const string RootAdmin= "RootAdmin";
    public const string TenantAdmin= "TenantAdmin";
    public const string SIGNAL_INBOUND_CHAT_OR_DATA = "HandleInboundChatOrData";
    public const string SIGNAL_INBOUND_EVENT = "HandleInboundEvent";
    public const string DefaultTenantId = "default";
    public const string UPDATE_INBOUND_CHAT_OR_DATA = "HandleInboundChatOrDataSync";
    public const string TaskTitleKey = "taskTitle";
    public const string TaskDescriptionKey = "taskDescription";
    public const string TaskActionsKey = "taskActions";

    /// <summary>
    /// Search attributes required for workflow queries.
    /// These are auto-registered when the server connects to Temporal.
    /// </summary>
    public static readonly Dictionary<string, IndexedValueType> RequiredSearchAttributes = new()
    {
        { TenantIdKey, IndexedValueType.Keyword },
        { AgentKey, IndexedValueType.Keyword },
        { UserIdKey, IndexedValueType.Keyword },
        { IdPostfixKey, IndexedValueType.Keyword }
    };
}