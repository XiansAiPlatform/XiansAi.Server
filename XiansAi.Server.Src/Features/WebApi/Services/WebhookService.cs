using XiansAi.Server.Shared.Data.Models;
using Shared.Auth;
using Features.WebApi.Repositories;
using MongoDB.Bson;
using XiansAi.Server.Shared.Data;

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

public class WebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly IDatabaseService _databaseService;
    private readonly ITenantContext _tenantContext;

    public WebhookService(
        ILogger<WebhookService> logger,
        IDatabaseService databaseService,
        ITenantContext tenantContext)
    {
        _logger = logger;
        _databaseService = databaseService;
        _tenantContext = tenantContext;
    }

    public async Task<IResult> GetAllWebhooks()
    {
        try
        {
            ValidateTenantContext();
            var webhookRepository = new WebhookRepository(await _databaseService.GetDatabase());
            var webhooks = await webhookRepository.GetAllForTenantAsync(_tenantContext.TenantId);
            return Results.Ok(webhooks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all webhooks");
            return Results.Problem("Failed to retrieve webhooks", statusCode: 500);
        }
    }

    public async Task<IResult> GetWebhooksByWorkflow(string workflowId)
    {
        try
        {
            ValidateTenantContext();
            var webhookRepository = new WebhookRepository(await _databaseService.GetDatabase());
            var webhooks = await webhookRepository.GetByWorkflowIdAsync(workflowId, _tenantContext.TenantId);
            return Results.Ok(webhooks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving webhooks for workflow {WorkflowId}", workflowId);
            return Results.Problem("Failed to retrieve webhooks", statusCode: 500);
        }
    }

    public async Task<IResult> GetWebhook(string webhookId)
    {
        try
        {
            ValidateTenantContext();
            var webhookRepository = new WebhookRepository(await _databaseService.GetDatabase());
            var webhook = await webhookRepository.GetByIdAsync(webhookId, _tenantContext.TenantId);
            if (webhook == null)
            {
                return Results.NotFound();
            }
            return Results.Ok(webhook);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving webhook {WebhookId}", webhookId);
            return Results.Problem("Failed to retrieve webhook", statusCode: 500);
        }
    }

    public async Task<IResult> CreateWebhook(WebhookCreateRequest request)
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

            var webhookRepository = new WebhookRepository(await _databaseService.GetDatabase());
            var createdWebhook = await webhookRepository.CreateAsync(webhook);
            return Results.Created($"/api/client/webhooks/{createdWebhook.Id}", createdWebhook);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating webhook");
            return Results.Problem("Failed to create webhook", statusCode: 500);
        }
    }

    public async Task<IResult> UpdateWebhook(string webhookId, WebhookUpdateRequest request)
    {
        try
        {
            ValidateTenantContext();
            var webhookRepository = new WebhookRepository(await _databaseService.GetDatabase());
            var webhook = await webhookRepository.GetByIdAsync(webhookId, _tenantContext.TenantId);
            if (webhook == null)
            {
                return Results.NotFound();
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

            await webhookRepository.UpdateAsync(webhook);
            return Results.Ok(webhook);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating webhook {WebhookId}", webhookId);
            return Results.Problem("Failed to update webhook", statusCode: 500);
        }
    }

    public async Task<IResult> DeleteWebhook(string webhookId)
    {
        try
        {
            ValidateTenantContext();
            var webhookRepository = new WebhookRepository(await _databaseService.GetDatabase());
            var result = await webhookRepository.DeleteAsync(webhookId, _tenantContext.TenantId);
            return result ? Results.NoContent() : Results.NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting webhook {WebhookId}", webhookId);
            return Results.Problem("Failed to delete webhook", statusCode: 500);
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
