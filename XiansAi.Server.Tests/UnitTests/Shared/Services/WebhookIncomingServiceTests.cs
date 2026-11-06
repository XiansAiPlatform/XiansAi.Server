using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Shared.Configuration;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

public class WebhookIncomingServiceTests
{
    private readonly Mock<IWebhookIncomingRepository> _incomings = new();
    private const int MaxPerWebhook = 25;

    private readonly Mock<IAppIntegrationRepository> _integrations = new();
    private readonly WebhookIncomingService _service;

    public WebhookIncomingServiceTests()
    {
        _service = new WebhookIncomingService(
            _incomings.Object,
            _integrations.Object,
            Options.Create(new WebhookIncomingsOptions { MaxPerWebhook = MaxPerWebhook }),
            NullLogger<WebhookIncomingService>.Instance);
    }

    [Fact]
    public async Task RecordSavesTheCallAgainstTheMatchingWebhookAndTrims()
    {
        var other = Webhook(webhookName: "Other");
        var webhook = Webhook();
        GivenWebhooks(other, webhook);
        WebhookIncoming? saved = null;
        _incomings.Setup(r => r.InsertAsync(It.IsAny<WebhookIncoming>())).Callback<WebhookIncoming>(i => saved = i);

        var headers = new HeaderDictionary
        {
            ["X-GitHub-Event"] = "pull_request",
            ["X-Repeated"] = new StringValues(["a", "b"])
        };
        var id = await _service.RecordAsync(Call(headers: headers));

        Assert.NotNull(saved);
        Assert.Equal(saved!.Id, id);
        Assert.Equal(webhook.Id, saved!.IntegrationId);
        Assert.Equal("tenant", saved.TenantId);
        Assert.Equal("request-1", saved.RequestId);
        Assert.Equal("tenant:agent:Integrator Workflow:activation", saved.WorkflowId);
        Assert.Equal("application/json", saved.ContentType);
        Assert.Equal("{\"action\":\"opened\"}", saved.Body);
        Assert.Equal(
            [("X-GitHub-Event", "pull_request"), ("X-Repeated", "a"), ("X-Repeated", "b")],
            saved.Headers.Select(h => (h.Name, h.Value)));
        _incomings.Verify(r => r.TrimAsync("tenant", webhook.Id, MaxPerWebhook), Times.Once);
    }

    [Fact]
    public async Task RecordMatchesOnApiKeyIdWhenTheUrlHasOne()
    {
        var otherKey = Webhook(apiKeyId: "key-2");
        var webhook = Webhook(apiKeyId: "key-1");
        GivenWebhooks(otherKey, webhook);

        await _service.RecordAsync(Call(apiKeyId: "key-1"));

        _incomings.Verify(r => r.InsertAsync(It.Is<WebhookIncoming>(i => i.IntegrationId == webhook.Id)), Times.Once);
    }

    [Fact]
    public async Task RecordSkipsCallsWithNoMatchingWebhook()
    {
        GivenWebhooks(Webhook(workflowName: "Another Workflow"));

        Assert.Null(await _service.RecordAsync(Call()));

        _incomings.Verify(r => r.InsertAsync(It.IsAny<WebhookIncoming>()), Times.Never);
    }

    [Fact]
    public async Task RecordSkipsCallsWithoutAnActivation()
    {
        await _service.RecordAsync(Call() with { ActivationName = null });

        _integrations.Verify(r => r.GetByAgentActivationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _incomings.Verify(r => r.InsertAsync(It.IsAny<WebhookIncoming>()), Times.Never);
    }

    [Fact]
    public async Task RecordNeverThrowsAndSkipsTheTrimWhenTheSaveFails()
    {
        GivenWebhooks(Webhook());
        _incomings.Setup(r => r.InsertAsync(It.IsAny<WebhookIncoming>()))
            .ThrowsAsync(new TimeoutException("boom"));

        Assert.Null(await _service.RecordAsync(Call()));

        _incomings.Verify(r => r.TrimAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RecordNeverThrowsWhenTheWebhookLookupFails()
    {
        _integrations.Setup(r => r.GetByAgentActivationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new TimeoutException());

        await _service.RecordAsync(Call());

        _incomings.Verify(r => r.InsertAsync(It.IsAny<WebhookIncoming>()), Times.Never);
    }

    [Fact]
    public async Task RecordStartedWorkflowsSavesTheIdsTheAgentReports()
    {
        var id = Guid.CreateVersion7();
        const string reply = "{\"status\":\"success\",\"workflowIds\":[\"t:a:Processing Workflow:act:req-1\",\"t:a:Processing Workflow:act:req-2\"]}";

        await _service.RecordStartedWorkflowsAsync("tenant", id, reply);

        _incomings.Verify(r => r.SetStartedWorkflowIdsAsync("tenant", id,
            It.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[]
            {
                "t:a:Processing Workflow:act:req-1",
                "t:a:Processing Workflow:act:req-2"
            }))), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[\"a\"]")]
    [InlineData("{\"status\":\"ignored\"}")]
    [InlineData("{\"workflowIds\":\"a\"}")]
    [InlineData("{\"workflowIds\":[]}")]
    public async Task RecordStartedWorkflowsIgnoresRepliesWithoutIds(string? reply)
    {
        await _service.RecordStartedWorkflowsAsync("tenant", Guid.CreateVersion7(), reply);

        _incomings.Verify(r => r.SetStartedWorkflowIdsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public async Task RecordStartedWorkflowsNeverThrows()
    {
        _incomings.Setup(r => r.SetStartedWorkflowIdsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>()))
            .ThrowsAsync(new TimeoutException());

        await _service.RecordStartedWorkflowsAsync("tenant", Guid.CreateVersion7(), "{\"workflowIds\":[\"w\"]}");
    }

    [Fact]
    public void ReadWorkflowIdsKeepsStringsInOrderWithoutRepeatsAndAnyCasing()
    {
        Assert.Equal(
            ["b", "a"],
            WebhookIncomingService.ReadWorkflowIds("{\"WorkflowIds\":[\"b\",1,null,\"\",\"a\",\"b\"]}"));
    }

    [Fact]
    public void ReadWorkflowIdsCapsTheCount()
    {
        var reply = "{\"workflowIds\":[" + string.Join(",", Enumerable.Range(0, 80).Select(i => $"\"w{i}\"")) + "]}";

        Assert.Equal(WebhookIncomingService.MaxStartedWorkflowIds, WebhookIncomingService.ReadWorkflowIds(reply).Count);
    }

    [Fact]
    public async Task ListReturnsANextCursorOnlyWhenThePageIsFull()
    {
        var webhook = GivenWebhookById();
        var rows = new List<WebhookIncomingSummary>
        {
            new(Guid.CreateVersion7(), "request-2", DateTime.UtcNow, false, false),
            new(Guid.CreateVersion7(), "request-1", DateTime.UtcNow.AddMinutes(-1), false, false)
        };
        _incomings.Setup(r => r.ListAsync("tenant", webhook.Id, null, 2)).ReturnsAsync(rows);
        _incomings.Setup(r => r.ListAsync("tenant", webhook.Id, null, 3)).ReturnsAsync(rows);

        var full = await _service.ListAsync("tenant", webhook.Id, null, 2);
        var partial = await _service.ListAsync("tenant", webhook.Id, null, 3);

        Assert.Equal(new WebhookIncomingCursor(rows[1].ReceivedAt, rows[1].Id), full.Data!.NextCursor);
        Assert.Null(partial.Data!.NextCursor);
    }

    [Fact]
    public async Task ListCapsThePageSize()
    {
        var webhook = GivenWebhookById();
        _incomings.Setup(r => r.ListAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WebhookIncomingCursor?>(), It.IsAny<int>()))
            .ReturnsAsync([]);

        await _service.ListAsync("tenant", webhook.Id, null, 10_000);

        _incomings.Verify(r => r.ListAsync("tenant", webhook.Id, null, WebhookIncomingRepository.MaxPageSize), Times.Once);
    }

    [Fact]
    public async Task ReadsAndDeletesRequireABuiltinWebhookOfTheSameTenant()
    {
        var otherTenant = GivenWebhookById(tenantId: "other-tenant");
        var slack = GivenWebhookById(platformId: "slack");
        var id = Guid.CreateVersion7();

        foreach (var integrationId in new[] { otherTenant.Id, slack.Id, "missing" })
        {
            Assert.Equal(StatusCode.NotFound, (await _service.ListAsync("tenant", integrationId, null, 10)).StatusCode);
            Assert.Equal(StatusCode.NotFound, (await _service.GetAsync("tenant", integrationId, id)).StatusCode);
            Assert.Equal(StatusCode.NotFound, (await _service.DeleteAsync("tenant", integrationId, id)).StatusCode);
            Assert.Equal(StatusCode.NotFound, (await _service.DeleteAllAsync("tenant", integrationId)).StatusCode);
        }

        _incomings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAndDeleteReportAMissingCall()
    {
        var webhook = GivenWebhookById();
        var id = Guid.CreateVersion7();

        Assert.Equal(StatusCode.NotFound, (await _service.GetAsync("tenant", webhook.Id, id)).StatusCode);
        Assert.Equal(StatusCode.NotFound, (await _service.DeleteAsync("tenant", webhook.Id, id)).StatusCode);
    }

    [Fact]
    public async Task GetMasksCredentialHeaderValues()
    {
        var webhook = GivenWebhookById();
        var id = Guid.CreateVersion7();
        _incomings.Setup(r => r.GetAsync("tenant", webhook.Id, id)).ReturnsAsync(new WebhookIncoming
        {
            Id = id,
            TenantId = "tenant",
            IntegrationId = webhook.Id,
            RequestId = "request-1",
            WorkflowId = "tenant:agent:Integrator Workflow:activation",
            Headers =
            [
                new() { Name = "X-GitHub-Event", Value = "issues" },
                new() { Name = "Authorization", Value = "Bearer secret" }
            ]
        });

        var result = await _service.GetAsync("tenant", webhook.Id, id);

        Assert.Equal(
            [("X-GitHub-Event", "issues"), ("Authorization", WebhookIncomingService.MaskedValue)],
            result.Data!.Headers.Select(h => (h.Name, h.Value)));
        Assert.Equal([false, true], result.Data.Headers.Select(h => h.Masked));
    }

    [Fact]
    public async Task DeleteRemovesOneCall()
    {
        var webhook = GivenWebhookById();
        var id = Guid.CreateVersion7();
        _incomings.Setup(r => r.DeleteAsync("tenant", webhook.Id, id)).ReturnsAsync(true);

        var result = await _service.DeleteAsync("tenant", webhook.Id, id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteAllReturnsTheNumberDeleted()
    {
        var webhook = GivenWebhookById();
        _incomings.Setup(r => r.DeleteByIntegrationAsync("tenant", webhook.Id)).ReturnsAsync(7);

        var result = await _service.DeleteAllAsync("tenant", webhook.Id);

        Assert.Equal(7, result.Data);
    }

    private void GivenWebhooks(params AppIntegration[] webhooks) =>
        _integrations.Setup(r => r.GetByAgentActivationAsync("tenant", "agent", "activation", "builtin_webhook"))
            .ReturnsAsync(webhooks.ToList());

    private AppIntegration GivenWebhookById(string tenantId = "tenant", string platformId = "builtin_webhook")
    {
        var webhook = Webhook(tenantId: tenantId, platformId: platformId);
        _integrations.Setup(r => r.GetByIdAsync(webhook.Id)).ReturnsAsync(webhook);
        return webhook;
    }

    private static BuiltinWebhookCall Call(string? apiKeyId = null, IHeaderDictionary? headers = null) => new(
        TenantId: "tenant",
        AgentName: "agent",
        ActivationName: "activation",
        WorkflowName: "Integrator Workflow",
        WebhookName: "Default",
        ApiKeyId: apiKeyId,
        WorkflowId: "tenant:agent:Integrator Workflow:activation",
        RequestId: "request-1",
        ContentType: "application/json",
        Headers: headers ?? new HeaderDictionary(),
        Body: "{\"action\":\"opened\"}");

    private static AppIntegration Webhook(
        string webhookName = "Default",
        string workflowName = "Integrator Workflow",
        string apiKeyId = "key-1",
        string tenantId = "tenant",
        string platformId = "builtin_webhook") => new()
    {
        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
        TenantId = tenantId,
        PlatformId = platformId,
        Name = $"Webhook-{webhookName}",
        AgentName = "agent",
        ActivationName = "activation",
        WorkflowId = "tenant:agent:Integrator Workflow:activation",
        Configuration = new()
        {
            ["webhookName"] = webhookName,
            ["workflowName"] = workflowName,
            ["apiKeyId"] = apiKeyId
        },
        Secrets = new AppIntegrationSecrets(),
        MappingConfig = new AppIntegrationMappingConfig(),
        CreatedBy = "user",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
