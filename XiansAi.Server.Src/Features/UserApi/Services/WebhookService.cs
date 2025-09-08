using Shared.Auth;
using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Shared.Repositories;
using Shared.Models;
using Temporalio.Exceptions;

namespace Features.UserApi.Services;

public interface IWebhookReceiverService
{
    Task<ServiceResult<WebhookResponse>> ProcessWebhook(
        string tenantId,
        string workflow,
        string methodName,
        IDictionary<string, string> queryParams,
        string body);
}

public class WebhookReceiverService : IWebhookReceiverService
{
    private readonly ITemporalClientFactory _temporalClientFactory;
    private readonly ITenantContext _tenantContext;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly ILogger<WebhookReceiverService> _logger;

    public WebhookReceiverService(
        ITemporalClientFactory temporalClientFactory,
        ITenantContext tenantContext,
        IApiKeyRepository apiKeyRepository,
        ILogger<WebhookReceiverService> logger)
    {
        _temporalClientFactory = temporalClientFactory ?? throw new ArgumentNullException(nameof(temporalClientFactory));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<WebhookResponse>> ProcessWebhook(
        string tenantId,
        string workflow,
        string methodName,
        IDictionary<string, string> queryParams,
        string body)
    {
        try
        {
            // Validate tenant context
            if (_tenantContext.TenantId != tenantId)
            {
                _logger.LogWarning("Tenant mismatch for webhook {TenantId}, {Workflow}, {MethodName}", tenantId, workflow, methodName);
                return ServiceResult<WebhookResponse>.Forbidden("Tenant mismatch");
            }

            _logger.LogInformation(
                "Processing webhook for tenant {TenantId}, workflow {Workflow}, method {Method}",
                tenantId, workflow, methodName);

            // Send the webhook update to temporal
            var result = await UpdateService.SendWebhookUpdate(
                workflow,
                methodName,
                queryParams,
                body,
                _tenantContext,
                _temporalClientFactory);

            _logger.LogInformation(
                "Successfully processed webhook for tenant {TenantId}, workflow {Workflow}, method {Method}",
                tenantId, workflow, methodName);

            return ServiceResult<WebhookResponse>.Success(result);
        }
        catch (WorkflowUpdateRpcTimeoutOrCanceledException ex)
        {
            _logger.LogWarning(ex, "Webhook update timed out for workflow {Workflow}, method {Method} - likely no workers available", workflow, methodName);
            return ServiceResult<WebhookResponse>.RequestTimeout(
                "The webhook update timed out. This typically means no workflow workers are available to process the request.",
                StatusCode.RequestTimeout);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation while processing webhook");
            return ServiceResult<WebhookResponse>.BadRequest(ex.Message, StatusCode.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook for workflow {Workflow}, method {Method}", workflow, methodName);
            return ServiceResult<WebhookResponse>.InternalServerError(
                $"An error occurred while processing the webhook. Exception: {ex.Message}",
                StatusCode.InternalServerError);
        }
    }
}
