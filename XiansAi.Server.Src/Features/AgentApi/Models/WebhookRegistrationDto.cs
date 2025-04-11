namespace XiansAi.Server.Features.AgentApi.Models
{
    public class WebhookRegistrationDto
    {
        public required string WorkflowId { get; set; }
        public required string CallbackUrl { get; set; }
        public required string EventType { get; set; }
    }
} 