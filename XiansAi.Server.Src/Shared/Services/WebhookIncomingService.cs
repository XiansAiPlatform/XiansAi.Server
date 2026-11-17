using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Shared.Configuration;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// A call that reached the builtin webhook and passed its checks. <c>Redelivery</c> is set only
/// when the server sends a recorded call again.
/// </summary>
public record BuiltinWebhookCall(
    string TenantId,
    string AgentName,
    string? ActivationName,
    string WorkflowName,
    string WebhookName,
    string? ApiKeyId,
    string WorkflowId,
    string RequestId,
    string? ContentType,
    IHeaderDictionary Headers,
    string Body,
    WebhookRedelivery? Redelivery = null);

/// <summary>One page of a builtin webhook's incoming calls, newest first.</summary>
public record WebhookIncomingPage(List<WebhookIncomingSummary> Items, WebhookIncomingCursor? NextCursor);

/// <summary>
/// Records calls received by builtin webhooks and lets admins read and delete them.
/// </summary>
public interface IWebhookIncomingService
{
    /// <summary>
    /// Saves the call against its builtin webhook and drops calls beyond
    /// <see cref="WebhookIncomingsOptions.MaxPerWebhook"/>. Never throws: a call that cannot be
    /// recorded is logged and the webhook carries on.
    /// </summary>
    /// <returns>The recorded call's id, or null when no builtin webhook matches the call or recording failed.</returns>
    Task<Guid?> RecordAsync(BuiltinWebhookCall call);

    Task<ServiceResult<WebhookIncomingPage>> ListAsync(string tenantId, string integrationId, WebhookIncomingCursor? after, int limit);

    /// <summary>One call. Credential header values are replaced by <see cref="WebhookIncomingService.MaskedValue"/>.</summary>
    Task<ServiceResult<WebhookIncoming>> GetAsync(string tenantId, string integrationId, Guid id);

    Task<ServiceResult<bool>> DeleteAsync(string tenantId, string integrationId, Guid id);

    /// <returns>The number of calls deleted.</returns>
    Task<ServiceResult<long>> DeleteAllAsync(string tenantId, string integrationId);
}

public class WebhookIncomingService : IWebhookIncomingService
{
    /// <summary>Shown in place of an encrypted header value; the real value never leaves the server.</summary>
    public const string MaskedValue = "********";

    private const string BuiltinWebhookPlatformId = "builtin_webhook";

    private readonly IWebhookIncomingRepository _incomings;
    private readonly IAppIntegrationRepository _integrations;
    private readonly WebhookIncomingsOptions _options;
    private readonly ILogger<WebhookIncomingService> _logger;

    public WebhookIncomingService(
        IWebhookIncomingRepository incomings,
        IAppIntegrationRepository integrations,
        IOptions<WebhookIncomingsOptions> options,
        ILogger<WebhookIncomingService> logger)
    {
        _incomings = incomings ?? throw new ArgumentNullException(nameof(incomings));
        _integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Guid?> RecordAsync(BuiltinWebhookCall call)
    {
        try
        {
            var integration = await FindIntegrationAsync(call);
            if (integration == null)
            {
                _logger.LogDebug(
                    "No builtin webhook matches call {RequestId} for agent {AgentName}, activation {ActivationName}, webhook {WebhookName}; not recorded",
                    call.RequestId, call.AgentName, call.ActivationName, call.WebhookName);
                return null;
            }

            var incoming = new WebhookIncoming
            {
                TenantId = call.TenantId,
                IntegrationId = integration.Id,
                RequestId = call.RequestId,
                ContentType = call.ContentType ?? string.Empty,
                Headers = ToHeaderList(call.Headers),
                Body = call.Body,
                WorkflowId = call.WorkflowId
            };

            // A redelivery is linked to the delivery it repeats, on the webhook that recorded it.
            if (call.Redelivery is { } redelivery && redelivery.IntegrationId == integration.Id)
            {
                incoming.RedeliveryOf = redelivery.RedeliveryOf;
                incoming.RedeliveryOfRequestId = redelivery.RedeliveryOfRequestId;
                incoming.TriggeredBy = redelivery.TriggeredBy;
            }

            await _incomings.InsertAsync(incoming);

            await _incomings.TrimAsync(call.TenantId, integration.Id, _options.MaxPerWebhook);
            return incoming.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record builtin webhook call {RequestId}", call.RequestId);
            return null;
        }
    }

    public async Task<ServiceResult<WebhookIncomingPage>> ListAsync(string tenantId, string integrationId, WebhookIncomingCursor? after, int limit)
    {
        if (!await IsBuiltinWebhookAsync(tenantId, integrationId))
            return ServiceResult<WebhookIncomingPage>.NotFound("Webhook not found");

        var pageSize = Math.Clamp(limit, 1, WebhookIncomingRepository.MaxPageSize);
        var items = await _incomings.ListAsync(tenantId, integrationId, after, pageSize);
        var next = items.Count == pageSize ? new WebhookIncomingCursor(items[^1].ReceivedAt, items[^1].Id) : null;
        return ServiceResult<WebhookIncomingPage>.Success(new WebhookIncomingPage(items, next));
    }

    public async Task<ServiceResult<WebhookIncoming>> GetAsync(string tenantId, string integrationId, Guid id)
    {
        if (!await IsBuiltinWebhookAsync(tenantId, integrationId))
            return ServiceResult<WebhookIncoming>.NotFound("Webhook not found");

        var incoming = await _incomings.GetAsync(tenantId, integrationId, id);
        if (incoming == null)
            return ServiceResult<WebhookIncoming>.NotFound("Incoming call not found");

        incoming.MaskCredentialHeaders(MaskedValue);
        return ServiceResult<WebhookIncoming>.Success(incoming);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string tenantId, string integrationId, Guid id)
    {
        if (!await IsBuiltinWebhookAsync(tenantId, integrationId))
            return ServiceResult<bool>.NotFound("Webhook not found");

        return await _incomings.DeleteAsync(tenantId, integrationId, id)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.NotFound("Incoming call not found");
    }

    public async Task<ServiceResult<long>> DeleteAllAsync(string tenantId, string integrationId)
    {
        if (!await IsBuiltinWebhookAsync(tenantId, integrationId))
            return ServiceResult<long>.NotFound("Webhook not found");

        return ServiceResult<long>.Success(await _incomings.DeleteByIntegrationAsync(tenantId, integrationId));
    }

    /// <summary>
    /// The builtin webhook whose URL produced this call. The URL carries no integration id, so it is
    /// matched on the values the URL was built from. Webhooks with identical values share a URL;
    /// the newest one is used, unless the call is a redelivery of one of the others.
    /// </summary>
    private async Task<AppIntegration?> FindIntegrationAsync(BuiltinWebhookCall call)
    {
        if (string.IsNullOrWhiteSpace(call.ActivationName))
            return null;

        var candidates = await _integrations.GetByAgentActivationAsync(
            call.TenantId, call.AgentName, call.ActivationName, BuiltinWebhookPlatformId);

        var matches = candidates.Where(i =>
            ConfigValue(i, "webhookName") == call.WebhookName &&
            ConfigValue(i, "workflowName") == call.WorkflowName &&
            (string.IsNullOrEmpty(call.ApiKeyId) || ConfigValue(i, "apiKeyId") == call.ApiKeyId)).ToList();

        return matches.FirstOrDefault(i => i.Id == call.Redelivery?.IntegrationId) ?? matches.FirstOrDefault();
    }

    private async Task<bool> IsBuiltinWebhookAsync(string tenantId, string integrationId)
    {
        var integration = await _integrations.GetByIdAsync(integrationId);
        return integration != null
            && integration.TenantId == tenantId
            && integration.PlatformId.Equals(BuiltinWebhookPlatformId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ConfigValue(AppIntegration integration, string key) =>
        integration.Configuration.TryGetValue(key, out var value) ? value?.ToString() : null;

    /// <summary>Every header line in order, one entry per value so repeated headers are kept.</summary>
    private static List<WebhookIncomingHeader> ToHeaderList(IHeaderDictionary headers) =>
        headers
            .SelectMany(h => h.Value.Select(v => new WebhookIncomingHeader { Name = h.Key, Value = v ?? string.Empty }))
            .ToList();
}
