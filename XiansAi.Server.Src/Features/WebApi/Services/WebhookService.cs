using XiansAi.Server.Shared.Data.Models;
using Shared.Auth;
using Features.WebApi.Repositories;
using MongoDB.Bson;
using XiansAi.Server.Shared.Data;
using Shared.Utils.Services;

namespace Features.WebApi.Services;

public class WebhookCreateRequest
{
    public required string WorkflowId { get; set; }
    public required string CallbackUrl { get; set; }
    public required string EventType { get; set; }
    public bool IsActive { get; set; } = true;
}

public class WebhookUpdateRequest
{
    public string? CallbackUrl { get; set; }
    public string? EventType { get; set; }
    public bool? IsActive { get; set; }
}

public class WebhookCreatedResult
{
    public Webhook Webhook { get; set; } = default!;
    public string Location { get; set; } = string.Empty;
}

public interface IWebhookService
{
    Task<ServiceResult<List<Webhook>>> GetAllWebhooks();
    Task<ServiceResult<List<Webhook>>> GetWebhooksByWorkflow(string workflowId);
    Task<ServiceResult<Webhook>> GetWebhook(string webhookId);
    Task<ServiceResult<WebhookCreatedResult>> CreateWebhook(WebhookCreateRequest request);
    Task<ServiceResult<Webhook>> UpdateWebhook(string webhookId, WebhookUpdateRequest request);
    Task<ServiceResult<bool>> DeleteWebhook(string webhookId);
}

public class WebhookService : IWebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly IWebhookRepository _webhookRepository;
    private readonly ITenantContext _tenantContext;

    public WebhookService(
        ILogger<WebhookService> logger,
        IWebhookRepository webhookRepository,
        ITenantContext tenantContext)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webhookRepository = webhookRepository ?? throw new ArgumentNullException(nameof(webhookRepository));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
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
