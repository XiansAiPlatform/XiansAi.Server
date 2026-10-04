using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.Mcp.Tools;

[McpServerToolType]
public sealed class WebhookTools(
    ITenantContext tenantContext,
    IPermissionsService permissions,
    IAgentRepository agents,
    IActivationRepository activations,
    IAppIntegrationService integrations)
{
    private async Task AuthorizeAsync(McpTarget target, bool write)
    {
        McpTarget.Validate(target);
        if (target.TenantId != tenantContext.TenantId) throw new McpException("Tenant access denied.");
        var permission = write
            ? await permissions.HasWritePermission(target.AgentName)
            : await permissions.HasReadPermission(target.AgentName);
        if (!permission.IsSuccess || !permission.Data) throw new McpException("Agent access denied.");
        var agentTask = agents.GetByNameAsync(target.AgentName, tenantContext.TenantId,
            tenantContext.LoggedInUser, tenantContext.UserRoles);
        var activationTask = activations.GetByNameAndAgentAsync(tenantContext.TenantId,
            target.AgentName, target.ActivationName);
        var agent = await agentTask;
        var activation = await activationTask;
        if (agent is null || activation is null)
            throw new McpException("Agent or activation not found.");
    }

    private static T Result<T>(ServiceResult<T> result)
    {
        if ((int)result.StatusCode >= 500) throw new McpException("Webhook operation failed.");
        if (!result.IsSuccess) throw new McpException(result.ErrorMessage ?? "Webhook operation failed.");
        return result.Data!;
    }

    [McpServerTool(Name = "list_webhooks", ReadOnly = true)]
    [Description("List inbound builtin webhooks for this activation. Returns configuration and exact IDs but omits credential-bearing webhook URLs. These webhooks synchronously invoke registered workflows; use list_workflows to understand available behavior.")]
    public async Task<List<WebhookSummary>> ListWebhooks(McpTarget target)
    {
        await AuthorizeAsync(target, false);
        return Result(await integrations.GetBuiltinWebhooksAsync(
                tenantContext.TenantId, target.ActivationName, target.AgentName))
            .Select(ToSummary).ToList();
    }

    [McpServerTool(Name = "create_webhook")]
    [Description("Create an inbound builtin webhook for a registered workflow in this activation. First use list_workflows for the exact workflow type and list_webhooks to avoid duplicate names. The returned relative URL contains credentials: disclose it only to the user and intended caller. Creation does not guarantee an agent worker is running.")]
    public async Task<CreatedWebhook> CreateWebhook(
        McpTarget target,
        [Description("Exact registered workflow type returned by list_workflows.")] string workflowType,
        [Description("Event name delivered to the workflow's webhook handler.")] string webhookName,
        [Description("Optional unique display name; defaults from the webhook and activation names.")] string? name = null,
        [Description("Participant attribution used for webhook messages; defaults to webhook.")] string? participantId = null,
        [Description("Seconds to wait synchronously for the workflow response, from 1 to 300.")] int timeoutInSeconds = 30)
    {
        await AuthorizeAsync(target, true);
        if (string.IsNullOrWhiteSpace(workflowType)) throw new McpException("Workflow type is required.");
        if (string.IsNullOrWhiteSpace(webhookName)) throw new McpException("Webhook name is required.");
        if (timeoutInSeconds is < 1 or > 300) throw new McpException("Timeout must be between 1 and 300 seconds.");
        var created = Result(await integrations.CreateBuiltinWebhookAsync(new CreateBuiltinWebhookRequest
        {
            AgentName = target.AgentName,
            ActivationName = target.ActivationName,
            WorkflowName = workflowType,
            WebhookName = webhookName,
            Name = name,
            ParticipantId = participantId,
            TimeoutInSeconds = timeoutInSeconds
        }, tenantContext.TenantId, tenantContext.LoggedInUser ?? "system"));
        return new CreatedWebhook(ToSummary(created), created.WebhookUrl);
    }

    [McpServerTool(Name = "delete_webhook", Destructive = true)]
    [Description("Permanently delete one inbound builtin webhook in this activation using its exact ID from list_webhooks. This revokes its associated credential. Set confirmed=true only after explicit user approval.")]
    public async Task<bool> DeleteWebhook(
        McpTarget target,
        [Description("Exact webhook ID returned by list_webhooks.")] string webhookId,
        [Description("Set true only after explicit user approval for permanent deletion.")] bool confirmed = false)
    {
        await AuthorizeAsync(target, true);
        if (!confirmed) throw new McpException("Explicit user confirmation is required before permanent deletion.");
        var webhooks = Result(await integrations.GetBuiltinWebhooksAsync(
            tenantContext.TenantId, target.ActivationName, target.AgentName));
        var matchingWebhook = webhooks.FirstOrDefault(webhook => webhook.Id == webhookId &&
            webhook.AgentName == target.AgentName && webhook.ActivationName == target.ActivationName);
        if (matchingWebhook is null)
            throw new McpException("Webhook not found in this activation. Use an exact ID from list_webhooks.");
        return Result(await integrations.DeleteBuiltinWebhookAsync(webhookId, tenantContext.TenantId));
    }

    private static WebhookSummary ToSummary(AppIntegrationResponse webhook) => new(
        webhook.Id,
        webhook.Name,
        webhook.AgentName,
        webhook.ActivationName,
        webhook.WorkflowId,
        Value<string>(webhook, "workflowName"),
        Value<string>(webhook, "webhookName"),
        Value<string>(webhook, "participantId"),
        Value<int?>(webhook, "timeoutInSeconds"),
        webhook.IsEnabled,
        webhook.CreatedAt);

    private static T? Value<T>(AppIntegrationResponse webhook, string key)
    {
        if (!webhook.Configuration.TryGetValue(key, out var value) || value is null) return default;
        if (value is T typed) return typed;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(
                System.Text.Json.JsonSerializer.Serialize(value));
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }
}

public sealed record WebhookSummary(
    string Id,
    string Name,
    string AgentName,
    string ActivationName,
    string WorkflowId,
    string? WorkflowName,
    string? WebhookName,
    string? ParticipantId,
    int? TimeoutInSeconds,
    bool IsEnabled,
    DateTime CreatedAt);

public sealed record CreatedWebhook(WebhookSummary Webhook, string WebhookUrl);
