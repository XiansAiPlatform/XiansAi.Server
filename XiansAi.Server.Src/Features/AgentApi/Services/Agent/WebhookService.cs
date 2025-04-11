using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiansAi.Server.Shared.Data.Models;
using XiansAi.Server.Features.AgentApi.Models;
using XiansAi.Server.Features.AgentApi.Repositories;
using Shared.Auth;

namespace XiansAi.Server.Features.AgentApi.Services.Agent
{
    public interface IWebhookService
    {
        Task<Webhook> RegisterWebhookAsync(WebhookRegistrationDto registration);
        Task<WebhookTriggerResult> TriggerWebhookAsync(string workflowId, string eventType, object payload);
        Task<bool> DeleteWebhookAsync(Guid webhookId);
        Task<Webhook> GetWebhookAsync(Guid webhookId);
        Task<WebhookTriggerResult> ManuallyTriggerWebhookAsync(WebhookTriggerDto triggerDto);
    }

    public class WebhookTriggerResult
    {
        public bool Success { get; set; }
        public int WebhooksTriggered { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class WebhookService : IWebhookService
    {
        private readonly ILogger<WebhookService> _logger;
        private readonly IWebhookRepository _webhookRepository;
        private readonly HttpClient _httpClient;
        private readonly ITenantContext _tenantContext;

        public WebhookService(
            ILogger<WebhookService> logger,
            IWebhookRepository webhookRepository,
            HttpClient httpClient,
            ITenantContext tenantContext)
        {
            _logger = logger;
            _webhookRepository = webhookRepository;
            _httpClient = httpClient;
            _tenantContext = tenantContext;
        }

        public async Task<Webhook> RegisterWebhookAsync(WebhookRegistrationDto registration)
        {
            ValidateTenantContext();

            var webhook = new Webhook
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                WorkflowId = registration.WorkflowId,
                CallbackUrl = registration.CallbackUrl,
                EventType = registration.EventType,
                Secret = GenerateWebhookSecret(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _tenantContext.LoggedInUser
            };

            return await _webhookRepository.CreateAsync(webhook);
        }

        public async Task<WebhookTriggerResult> TriggerWebhookAsync(string workflowId, string eventType, object payload)
        {
            ValidateTenantContext();

            var webhooks = await _webhookRepository.GetByWorkflowIdAsync(workflowId, _tenantContext.TenantId);
            var result = new WebhookTriggerResult();

            foreach (var webhook in webhooks.Where(w => w.EventType == eventType))
            {
                try
                {
                    await TriggerSingleWebhookAsync(webhook, payload);
                    result.WebhooksTriggered++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to trigger webhook {webhook.Id}: {ex.Message}");
                    _logger.LogError(ex, "Failed to trigger webhook {WebhookId} for workflow {WorkflowId}", 
                        webhook.Id, workflowId);
                }
            }

            result.Success = result.WebhooksTriggered > 0;
            return result;
        }

        public async Task<WebhookTriggerResult> ManuallyTriggerWebhookAsync(WebhookTriggerDto triggerDto)
        {
            ValidateTenantContext();
            return await TriggerWebhookAsync(triggerDto.WorkflowId, triggerDto.EventType, triggerDto.Payload);
        }

        public async Task<bool> DeleteWebhookAsync(Guid webhookId)
        {
            ValidateTenantContext();
            return await _webhookRepository.DeleteAsync(webhookId, _tenantContext.TenantId);
        }

        public async Task<Webhook> GetWebhookAsync(Guid webhookId)
        {
            ValidateTenantContext();
            return await _webhookRepository.GetByIdAsync(webhookId, _tenantContext.TenantId);
        }

        private async Task TriggerSingleWebhookAsync(Webhook webhook, object payload)
        {
            var signature = GenerateSignature(payload, webhook.Secret);
            var request = new HttpRequestMessage(HttpMethod.Post, webhook.CallbackUrl)
            {
                Content = JsonContent.Create(new
                {
                    workflowId = webhook.WorkflowId,
                    eventType = webhook.EventType,
                    payload = payload,
                    triggeredAt = DateTime.UtcNow,
                    isManualTrigger = false
                })
            };

            request.Headers.Add("X-Webhook-Signature", signature);
            request.Headers.Add("X-Webhook-ID", webhook.Id.ToString());

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            webhook.LastTriggeredAt = DateTime.UtcNow;
            await _webhookRepository.UpdateAsync(webhook);
        }

        private string GenerateWebhookSecret()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }

        private string GenerateSignature(object payload, string secret)
        {
            var json = JsonSerializer.Serialize(payload);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToBase64String(hash);
        }

        private void ValidateTenantContext()
        {
            if (string.IsNullOrEmpty(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("TenantId is required for webhook operations");
            }
        }
    }
} 