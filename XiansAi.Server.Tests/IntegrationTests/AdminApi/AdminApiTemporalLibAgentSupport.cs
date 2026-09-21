using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shared.Auth;
using Shared.Data.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Admin HTTP helpers shared by Lib-backed Temporal cycles (Echo, Knowledge, Secret Vault, Document DB, Webhooks, Files, Custom workflows, Schedules, HITL tasks, Cross-agent, Activations SDK, Metrics).
/// </summary>
public abstract partial class AdminApiTemporalIntegrationTestBase
{
    protected void BindTenantContext(string tenantId, string userId)
    {
        var tenantContext = _factory.Services.GetRequiredService<ITenantContext>();
        tenantContext.TenantId = tenantId;
        tenantContext.LoggedInUser = userId;
        tenantContext.ParticipantId = userId;
        tenantContext.UserRoles = [SystemRoles.SysAdmin, SystemRoles.TenantAdmin, SystemRoles.TenantUser];
        tenantContext.AuthorizedTenantIds = [tenantId];
    }

    protected async Task WaitForTemplateAsync(string agentName)
    {
        var encodedAgent = Uri.EscapeDataString(agentName);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await GetAsync($"/api/v1/admin/agentTemplates/by-name/{encodedAgent}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Xians.Lib did not upload the system template '{agentName}' in time.");
    }

    protected async Task DeployLibTemplateAsync(string tenantId, string agentName)
    {
        var encodedAgent = Uri.EscapeDataString(agentName);
        var deploy = await PostAsJsonAsync(
            $"/api/v1/admin/agentTemplates/by-name/{encodedAgent}/deploy?tenantId={Uri.EscapeDataString(tenantId)}",
            new { });
        Assert.Equal(HttpStatusCode.OK, deploy.StatusCode);
    }

    protected async Task<string> ActivateLibAgentAsync(string tenantId, string agentName, string activationName)
    {
        var (activationId, _) = await ActivateLibAgentWithWorkflowsAsync(tenantId, agentName, activationName);
        return activationId;
    }

    protected async Task<(string ActivationId, IReadOnlyList<string> WorkflowIds)> ActivateLibAgentWithWorkflowsAsync(
        string tenantId,
        string agentName,
        string activationName)
    {
        var create = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations", new
        {
            name = activationName,
            agentName,
            participantId = _adminUserId
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var activationId = createdJson.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(activationId));

        var activate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}/activate",
            new { });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        using var activateJson = JsonDocument.Parse(await activate.Content.ReadAsStringAsync());
        var workflowIds = new List<string>();
        if (activateJson.RootElement.TryGetProperty("workflowIds", out var ids) &&
            ids.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ids.EnumerateArray())
            {
                var workflowId = item.GetString();
                if (!string.IsNullOrWhiteSpace(workflowId))
                {
                    workflowIds.Add(workflowId);
                }
            }
        }

        return (activationId!, workflowIds);
    }

    protected async Task RemoveLibActivationAsync(string tenantId, string activationId)
    {
        var deactivate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}/deactivate",
            new { });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var deleteActivation = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}");
        Assert.Equal(HttpStatusCode.OK, deleteActivation.StatusCode);
    }

    protected async Task RemoveLibDeploymentAsync(string tenantId, string agentName)
    {
        var encodedAgent = Uri.EscapeDataString(agentName);
        var deleteDeployment = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentDeployments/{encodedAgent}?forceDelete=true");
        Assert.Equal(HttpStatusCode.OK, deleteDeployment.StatusCode);
    }

    protected async Task RemoveLibTemplateAsync(string agentName)
    {
        var encodedAgent = Uri.EscapeDataString(agentName);
        var deleteTemplate = await DeleteAsync(
            $"/api/v1/admin/agentTemplates/by-name/{encodedAgent}?cleanActivations=true");
        Assert.Equal(HttpStatusCode.NoContent, deleteTemplate.StatusCode);
    }

    protected async Task<(bool Found, string LastBody)> WaitForHistoryContainsAsync(
        string tenantId,
        string agentName,
        string activationName,
        string participantId,
        string expectedText)
    {
        var query =
            $"agentName={Uri.EscapeDataString(agentName)}" +
            $"&activationName={Uri.EscapeDataString(activationName)}" +
            $"&participantId={Uri.EscapeDataString(participantId)}";
        var lastBody = string.Empty;

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var history = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history?{query}");
            lastBody = await history.Content.ReadAsStringAsync();
            if (history.StatusCode == HttpStatusCode.OK &&
                HistoryMessageTextContains(lastBody, expectedText))
            {
                return (true, lastBody);
            }

            await Task.Delay(250);
        }

        return (false, lastBody);
    }

    private static bool HistoryMessageTextContains(string body, string expectedText)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                return body.Contains(expectedText, StringComparison.Ordinal);
            }

            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var text) &&
                    text.GetString()?.Contains(expectedText, StringComparison.Ordinal) == true)
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return body.Contains(expectedText, StringComparison.Ordinal);
        }
    }

    protected async Task AssertAgentRepliesWithAsync(
        string tenantId,
        string agentName,
        string activationName,
        string expectedText,
        string userText = "what knowledge do you have?",
        string? participantId = null)
    {
        BindTenantContext(tenantId, _adminUserId!);
        participantId ??= $"reader-{Guid.NewGuid():N}@example.com";
        var send = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/send", new
        {
            agentName,
            activationName,
            participantId,
            text = userText
        });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var (found, historyBody) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, expectedText);
        Assert.True(
            found,
            $"Expected '{expectedText}' from {agentName} on {tenantId}/{activationName}. History: {historyBody}");
    }

    protected async Task<Knowledge> GetLatestKnowledgeAsync(
        string tenantId,
        string agentName,
        string knowledgeName,
        string? activationName = null)
    {
        var query =
            $"name={Uri.EscapeDataString(knowledgeName)}" +
            $"&agentName={Uri.EscapeDataString(agentName)}";
        if (!string.IsNullOrEmpty(activationName))
        {
            query += $"&activationName={Uri.EscapeDataString(activationName)}";
        }

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/latest?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var knowledge = await ReadAsJsonAsync<Knowledge>(response);
        Assert.NotNull(knowledge);
        return knowledge!;
    }

    protected async Task<Knowledge> OverrideKnowledgeAsync(
        string tenantId,
        string knowledgeId,
        string level,
        string? activationName = null)
    {
        var url = $"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledgeId}/override/{level}";
        if (string.Equals(level, "activation", StringComparison.OrdinalIgnoreCase))
        {
            Assert.False(string.IsNullOrWhiteSpace(activationName));
            url += $"?activationName={Uri.EscapeDataString(activationName!)}";
        }

        var response = await PostAsJsonAsync(url, new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsJsonAsync<Knowledge>(response);
        Assert.NotNull(created);
        return created!;
    }

    protected async Task PatchKnowledgeContentAsync(string tenantId, string knowledgeId, string content)
    {
        var response = await PatchAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledgeId}",
            new { content, type = "text" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
