using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiansAi.Server.Shared.Data.Models;
using Shared.Auth;
using Features.WebApi.Repositories;

namespace Features.WebApi.Services.Web
{
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

    public class WebhookEndpoint
    {
        private readonly ILogger<WebhookEndpoint> _logger;
        private readonly IWebhookRepository _webhookRepository;
        private readonly ITenantContext _tenantContext;

        public WebhookEndpoint(
            ILogger<WebhookEndpoint> logger,
            IWebhookRepository webhookRepository,
            ITenantContext tenantContext)
        {
            _logger = logger;
            _webhookRepository = webhookRepository;
            _tenantContext = tenantContext;
        }

        public async Task<IResult> GetAllWebhooks()
        {
            try
            {
                ValidateTenantContext();
                var webhooks = await _webhookRepository.GetAllForTenantAsync(_tenantContext.TenantId);
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
                var webhooks = await _webhookRepository.GetByWorkflowIdAsync(workflowId, _tenantContext.TenantId);
                return Results.Ok(webhooks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving webhooks for workflow {WorkflowId}", workflowId);
                return Results.Problem("Failed to retrieve webhooks", statusCode: 500);
            }
        }

        public async Task<IResult> GetWebhook(Guid webhookId)
        {
            try
            {
                ValidateTenantContext();
                var webhook = await _webhookRepository.GetByIdAsync(webhookId, _tenantContext.TenantId);
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
                    Id = Guid.NewGuid(),
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
                return Results.Created($"/api/client/webhooks/{createdWebhook.Id}", createdWebhook);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating webhook");
                return Results.Problem("Failed to create webhook", statusCode: 500);
            }
        }

        public async Task<IResult> UpdateWebhook(Guid webhookId, WebhookUpdateRequest request)
        {
            try
            {
                ValidateTenantContext();

                var webhook = await _webhookRepository.GetByIdAsync(webhookId, _tenantContext.TenantId);
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

                await _webhookRepository.UpdateAsync(webhook);
                return Results.Ok(webhook);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating webhook {WebhookId}", webhookId);
                return Results.Problem("Failed to update webhook", statusCode: 500);
            }
        }

        public async Task<IResult> DeleteWebhook(Guid webhookId)
        {
            try
            {
                ValidateTenantContext();
                var result = await _webhookRepository.DeleteAsync(webhookId, _tenantContext.TenantId);
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
} 