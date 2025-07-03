using XiansAi.Server.Shared.Data.Models;
using Shared.Auth;
using MongoDB.Bson;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Shared.Repositories;

namespace Shared.Services;

public class WebhookCreateRequest
{
    [Required(ErrorMessage = "WorkflowId is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "WorkflowId must be between 1 and 100 characters")]
    public required string WorkflowId { get; set; }
    
    [Required(ErrorMessage = "CallbackUrl is required")]
    [Url(ErrorMessage = "CallbackUrl must be a valid URL")]
    public required string CallbackUrl { get; set; }
    
    [Required(ErrorMessage = "EventType is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "EventType must be between 1 and 100 characters")]
    public required string EventType { get; set; }
    
    public bool IsActive { get; set; } = true;
}

public class WebhookUpdateRequest
{
    [Url(ErrorMessage = "CallbackUrl must be a valid URL")]
    public string? CallbackUrl { get; set; }
    
    [StringLength(100, MinimumLength = 1, ErrorMessage = "EventType must be between 1 and 100 characters")]
    public string? EventType { get; set; }
    
    public bool? IsActive { get; set; }
}

public class WebhookCreatedResult
{
    public Webhook Webhook { get; set; } = default!;
    public string Location { get; set; } = string.Empty;
}

public class WebhookTriggerResult
{
    public bool Success { get; set; }
    public int WebhooksTriggered { get; set; }
    public List<string> Errors { get; set; } = new List<string>();
}

public class WebhookRegistrationDto
{
    public required string WorkflowId { get; set; }
    public required string CallbackUrl { get; set; }
    public required string EventType { get; set; }
}

public class WebhookTriggerDto
{
    public required string WebhookId { get; set; }
    public required string WorkflowId { get; set; }
    public required string EventType { get; set; }
    public required object Payload { get; set; }
}

public class SendMessageWebhookDto
{
    public required ChatOrDataRequest Request { get; set; }
    public required MessageType MessageType { get; set; }
}

public class ChatHistoryWebhookDto
{
    public required string WorkflowId { get; set; }
    public required string ParticipantId { get; set; }
    public required int Page { get; set; }
    public required int PageSize { get; set; }
}
public interface IWebhookService
{
    Task<ServiceResult<List<Webhook>>> GetAllWebhooks();
    Task<ServiceResult<List<Webhook>>> GetWebhooksByWorkflow(string workflowId);
    Task<ServiceResult<Webhook>> GetWebhook(string webhookId);
    Task<ServiceResult<WebhookCreatedResult>> CreateWebhook(WebhookCreateRequest request);
    Task<ServiceResult<Webhook>> UpdateWebhook(string webhookId, WebhookUpdateRequest request);
    Task<ServiceResult<bool>> DeleteWebhook(string webhookId);
    Task<WebhookTriggerResult> TriggerWebhookAsync(string workflowId, string eventType, object payload);
    Task<WebhookTriggerResult> ManuallyTriggerWebhookAsync(WebhookTriggerDto triggerDto);
}

public class WebhookService : IWebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly IWebhookRepository _webhookRepository;
    private readonly ITenantContext _tenantContext;
    private readonly HttpClient _httpClient;
    private readonly IMessageService _messageService;

    public WebhookService(
        ILogger<WebhookService> logger,
        IWebhookRepository webhookRepository,
        ITenantContext tenantContext,
        HttpClient httpClient,
        IMessageService messageService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webhookRepository = webhookRepository ?? throw new ArgumentNullException(nameof(webhookRepository));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _httpClient = httpClient;
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
    }

    public async Task<ServiceResult<List<Webhook>>> GetAllWebhooks()
    {
        try
        {
            ValidateTenantContext();
            var webhooks = await _webhookRepository.GetAllForTenantAsync(_tenantContext.TenantId);
            return ServiceResult<List<Webhook>>.Success(webhooks.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all webhooks");
            return ServiceResult<List<Webhook>>.InternalServerError("Failed to retrieve webhooks");
        }
    }

    public async Task<ServiceResult<List<Webhook>>> GetWebhooksByWorkflow(string workflowId)
    {
        try
        {
            ValidateTenantContext();
            var webhooks = await _webhookRepository.GetByWorkflowIdAsync(workflowId, _tenantContext.TenantId);
            return ServiceResult<List<Webhook>>.Success(webhooks.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving webhooks for workflow {WorkflowId}", workflowId);
            return ServiceResult<List<Webhook>>.InternalServerError("Failed to retrieve webhooks");
        }
    }

    public async Task<ServiceResult<Webhook>> GetWebhook(string webhookId)
    {
        try
        {
            ValidateTenantContext();
            var webhook = await _webhookRepository.GetByIdAsync(webhookId, _tenantContext.TenantId);
            if (webhook == null)
            {
                return ServiceResult<Webhook>.NotFound("Webhook not found");
            }
            return ServiceResult<Webhook>.Success(webhook);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving webhook {WebhookId}", webhookId);
            return ServiceResult<Webhook>.InternalServerError("Failed to retrieve webhook");
        }
    }

    public async Task<ServiceResult<WebhookCreatedResult>> CreateWebhook(WebhookCreateRequest request)
    {
        try
        {
            ValidateTenantContext();

            var webhook = new Webhook
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = _tenantContext.TenantId,
                WorkflowId = request.WorkflowId,
                CallbackUrl = request.CallbackUrl,
                EventType = request.EventType,
                Secret = GenerateWebhookSecret(),
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _tenantContext.LoggedInUser
            };

            var createdWebhook = await _webhookRepository.CreateAsync(webhook);
            var result = new WebhookCreatedResult
            {
                Webhook = createdWebhook,
                Location = $"/api/client/webhooks/{createdWebhook.Id}"
            };
            return ServiceResult<WebhookCreatedResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating webhook");
            return ServiceResult<WebhookCreatedResult>.InternalServerError("Failed to create webhook");
        }
    }

    public async Task<ServiceResult<Webhook>> UpdateWebhook(string webhookId, WebhookUpdateRequest request)
    {
        try
        {
            ValidateTenantContext();
            var webhook = await _webhookRepository.GetByIdAsync(webhookId, _tenantContext.TenantId);
            if (webhook == null)
            {
                return ServiceResult<Webhook>.NotFound("Webhook not found");
            }

            if (request.CallbackUrl != null)
            {
                webhook.CallbackUrl = request.CallbackUrl;
            }

            if (request.EventType != null)
            {
                webhook.EventType = request.EventType;
            }

            if (request.IsActive.HasValue)
            {
                webhook.IsActive = request.IsActive.Value;
            }

            await _webhookRepository.UpdateAsync(webhook);
            return ServiceResult<Webhook>.Success(webhook);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating webhook {WebhookId}", webhookId);
            return ServiceResult<Webhook>.InternalServerError("Failed to update webhook");
        }
    }

    public async Task<ServiceResult<bool>> DeleteWebhook(string webhookId)
    {
        try
        {
            ValidateTenantContext();
            var result = await _webhookRepository.DeleteAsync(webhookId, _tenantContext.TenantId);
            if (!result)
            {
                return ServiceResult<bool>.NotFound("Webhook not found");
            }
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting webhook {WebhookId}", webhookId);
            return ServiceResult<bool>.InternalServerError("Failed to delete webhook");
        }
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
        if (triggerDto.EventType == "message.send")
        {
            SendMessageWebhookDto sendMessageDto = null;
            try
            {
                // If Payload is a JsonElement or string, deserialize accordingly
                var payloadElement = triggerDto.Payload as JsonElement?
                    ?? JsonDocument.Parse(triggerDto.Payload.ToString()).RootElement;

                sendMessageDto = JsonSerializer.Deserialize<SendMessageWebhookDto>(payloadElement.GetRawText());
            }
            catch (Exception ex)
            {
                return new WebhookTriggerResult
                {
                    Success = false,
                    Errors = new List<string> { "Invalid payload format", ex.Message }
                };
            }

            if (sendMessageDto == null)
            {
                return new WebhookTriggerResult
                {
                    Success = false,
                    Errors = new List<string> { "Payload could not be deserialized to SendMessageWebhookDto." }
                };
            }

            var inboundResult = await _messageService.ProcessIncomingMessage(sendMessageDto.Request, sendMessageDto.MessageType);
            // Optionally, handle inboundResult here
            return await TriggerWebhookAsync(triggerDto.WorkflowId, triggerDto.EventType, inboundResult);
        }
        else if (triggerDto.EventType == "message.history")
        {
            ChatHistoryWebhookDto chatHistoryDto = null;
            try
            {
                // If Payload is a JsonElement or string, deserialize accordingly
                var payloadElement = triggerDto.Payload as JsonElement?
                    ?? JsonDocument.Parse(triggerDto.Payload.ToString()).RootElement;

                chatHistoryDto = JsonSerializer.Deserialize<ChatHistoryWebhookDto>(payloadElement.GetRawText());
            }
            catch (Exception ex)
            {
                return new WebhookTriggerResult
                {
                    Success = false,
                    Errors = new List<string> { "Invalid payload format", ex.Message }
                };
            }
            if (chatHistoryDto == null)
            {
                return new WebhookTriggerResult
                {
                    Success = false,
                    Errors = new List<string> { "Payload could not be deserialized to ChatHistoryWebhookDto." }
                };
            }
            var historyResult = await _messageService.GetThreadHistoryAsync(
                chatHistoryDto.WorkflowId, chatHistoryDto.ParticipantId, chatHistoryDto.Page, chatHistoryDto.PageSize);
            if (!historyResult.IsSuccess)
            {
                return new WebhookTriggerResult
                {
                    Success = false,
                    Errors = new List<string> { "Failed to retrieve chat history: " + historyResult.ErrorMessage }
                };
            }
            // Trigger webhook with the chat history result
            return await TriggerWebhookAsync(chatHistoryDto.WorkflowId, triggerDto.EventType, historyResult.Data);
        }
        return await TriggerWebhookAsync(triggerDto.WorkflowId, triggerDto.EventType, triggerDto.Payload);
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

    private string GenerateSignature(object payload, string secret)
    {
        var json = JsonSerializer.Serialize(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hash);
    }

    private string GenerateWebhookSecret()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    private void ValidateTenantContext()
    {
        if (string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            throw new InvalidOperationException("TenantId is required for webhook operations");
        }
    }
}
