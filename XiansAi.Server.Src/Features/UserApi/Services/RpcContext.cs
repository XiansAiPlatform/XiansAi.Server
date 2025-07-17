public class RpcContext
{
    public required string ParticipantId { get; set; }
    public required string WorkflowId { get; set; }
    public required string WorkflowType { get; set; }
    public required string Agent { get; set; }
    public string? Authorization { get; set; }
    public string? Metadata { get; set; }
    public required string TenantId { get; set; }
}