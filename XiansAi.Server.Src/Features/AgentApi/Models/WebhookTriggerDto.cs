namespace Features.AgentApi.Models
{
    public class WebhookTriggerDto
    {
        public required string WorkflowId { get; set; }
        public required string EventType { get; set; }
        public required object Payload { get; set; }
    }
} 