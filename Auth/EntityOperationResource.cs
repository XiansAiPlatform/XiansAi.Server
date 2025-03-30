namespace XiansAi.Server.Auth
{
    public class EntityOperationResource
    {
        public string EntityId { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty; // Flow, Activity, etc.
        public string TenantId { get; set; } = string.Empty;
    }
} 