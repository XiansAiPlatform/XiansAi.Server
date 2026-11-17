using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;
using WebhookResponse = global::Shared.Models.WebhookResponse;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

public class WebhookRedeliveryServiceTests
{
    private const string WebhookPath = "/api/user/webhooks/builtin?apikeyId=key-1&timeoutSeconds=2&agentName=agent&workflowName=Integrator%20Workflow&webhookName=GitHub&activationName=activation&scope=ci";
    private const string Body = "{\"action\":\"opened\",\"title\":\"café\"}";

    private readonly Mock<IWebhookIncomingRepository> _incomings = new();
    private readonly Mock<IAppIntegrationRepository> _integrations = new();
    private readonly Mock<IWebhookIncomingService> _webhookIncomingService = new();
    private readonly Mock<IActivationValidationService> _activationValidation = new();
    private readonly Mock<IMessageService> _messageService = new();
    private readonly Mock<IPendingRequestService> _pendingRequests = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IAuditLogService> _auditLog = new();

    private readonly List<BuiltinWebhookCall> _recorded = new();
    private readonly List<ChatOrDataRequest> _sent = new();
    private readonly List<TimeSpan> _waits = new();
    private readonly List<object?> _audits = new();
    private readonly Guid _deliveryId = Guid.CreateVersion7();
    private string? _authorizationWhenSent = "not sent";

    public WebhookRedeliveryServiceTests()
    {
        _tenantContext.SetupProperty(t => t.Authorization, "admin-token");
        _activationValidation
            .Setup(v => v.ValidateActivationAsync("tenant", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult.Success());
        _webhookIncomingService
            .Setup(s => s.RecordAsync(It.IsAny<BuiltinWebhookCall>()))
            .Callback<BuiltinWebhookCall>(_recorded.Add)
            .ReturnsAsync(_deliveryId);
        _messageService
            .Setup(m => m.ProcessIncomingMessage(It.IsAny<ChatOrDataRequest>(), MessageType.Webhook))
            .Callback<ChatOrDataRequest, MessageType>((request, _) =>
            {
                _sent.Add(request);
                _authorizationWhenSent = _tenantContext.Object.Authorization;
            })
            .ReturnsAsync(ServiceResult<string>.Success("thread-1"));
        GivenTheAgentAnswers(new WebhookResponse(HttpStatusCode.Accepted));
        _auditLog
            .Setup(a => a.RecordEntryAsync(
                DomainEventTypes.IntegrationWebhookRedelivered, It.IsAny<string?>(), null, It.IsAny<object?>(), "tenant"))
            .Callback<string, string?, string?, object?, string?>((_, _, _, details, _) => _audits.Add(details))
            .ReturnsAsync(ServiceResult<AuditLogEntry>.BadRequest("not stored in tests"));
    }

    /// <summary>A value from an audit entry's details (an anonymous object).</summary>
    private static object? Detail(object? details, string name) =>
        details!.GetType().GetProperty(name)!.GetValue(details);

    [Fact]
    public async Task StartsTheAgentsWorkflowWithTheStoredCallAndTheValuesFromTheWebhooksUrl()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.True(result.IsSuccess);
        var request = Assert.Single(_sent);
        Assert.Equal("tenant:agent:Integrator Workflow:activation", request.ActivationWorkflowId);
        Assert.StartsWith("tenant:agent:Integrator Workflow:activation", request.WorkflowId);
        Assert.NotEqual(original.RequestId, request.RequestId);
        Assert.Equal("webhook", request.ParticipantId);
        Assert.Equal("GitHub", request.Text);
        Assert.Equal(Body, request.Data);
        Assert.Equal("ci", request.Scope);
        Assert.Equal("webhook:builtin:GitHub", request.Origin);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(_waits));
    }

    [Fact]
    public async Task RecordsTheCallAsANewDeliveryLinkedToTheOriginal()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);

        await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        var call = Assert.Single(_recorded);
        var request = Assert.Single(_sent);
        Assert.Equal("agent", call.AgentName);
        Assert.Equal("Integrator Workflow", call.WorkflowName);
        Assert.Equal("GitHub", call.WebhookName);
        Assert.Equal("activation", call.ActivationName);
        Assert.Equal("key-1", call.ApiKeyId);
        Assert.Equal(request.WorkflowId, call.WorkflowId);
        Assert.Equal(request.RequestId, call.RequestId);
        Assert.Equal(Body, call.Body);
        Assert.Equal("application/json; charset=utf-8", call.ContentType);
        Assert.Equal("pull_request", call.Headers["X-GitHub-Event"]);
        Assert.Equal(new[] { "a", "b" }, call.Headers["X-Repeated"].ToArray());
        Assert.Equal(original.Id.ToString(), call.Headers[WebhookRedeliveryService.RedeliveryOfHeader]);

        Assert.Equal(webhook.Id, call.Redelivery!.IntegrationId);
        Assert.Equal(original.Id, call.Redelivery.RedeliveryOf);
        Assert.Equal("request-1", call.Redelivery.RedeliveryOfRequestId);
        Assert.Equal("dev@example.com", call.Redelivery.TriggeredBy);
    }

    [Fact]
    public async Task TheAgentGetsTheOriginalApiKeyAndNeverTheAdminsCredential()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);

        await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal("sk-Xnai-original", Assert.Single(_sent).Authorization);
        Assert.Null(_authorizationWhenSent);
    }

    [Fact]
    public async Task ACallThatDidNotAuthenticateWithAnApiKeyHeaderPassesNoCredential()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook, i => i.Headers.RemoveAll(h => h.Name == "Authorization"));

        await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Null(Assert.Single(_sent).Authorization);
        Assert.Null(_authorizationWhenSent);
    }

    [Fact]
    public async Task ReturnsTheNewDeliveryAndWhatTheAgentAnswered()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(_deliveryId, result.Data!.DeliveryId);
        Assert.Equal(original.Id, result.Data.RedeliveryOf);
        Assert.Equal(202, result.Data.StatusCode);
        var audit = Assert.Single(_audits);
        Assert.Equal(_deliveryId.ToString(), Detail(audit, "deliveryId"));
        Assert.Equal(original.Id.ToString(), Detail(audit, "redeliveryOf"));
        Assert.Equal(202, Detail(audit, "statusCode"));
    }

    [Fact]
    public async Task AnAgentThatDoesNotAnswerInTimeIsReportedAs408()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        _pendingRequests
            .Setup(p => p.WaitForResponseAsync<ConversationMessage>(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), MessageType.Webhook, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationMessage?)null);

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal(_deliveryId, result.Data!.DeliveryId);
        Assert.Equal(408, result.Data.StatusCode);
    }

    [Fact]
    public async Task AnInactiveActivationIsAConflictAndRunsNothing()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        _activationValidation
            .Setup(v => v.ValidateActivationAsync("tenant", "agent", "activation", "Integrator Workflow"))
            .ReturnsAsync(ServiceResult.Failure("Activation is not active"));

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        Assert.Empty(_recorded);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task ARedeliveryOfARedeliveryPointsAtTheFirstDelivery()
    {
        var webhook = GivenWebhook();
        var first = Guid.CreateVersion7();
        var redelivery = GivenIncoming(webhook, i =>
        {
            i.RedeliveryOf = first;
            i.RedeliveryOfRequestId = "first-request";
            i.Headers.Add(new WebhookIncomingHeader { Name = WebhookRedeliveryService.RedeliveryOfHeader, Value = first.ToString() });
        });

        await Service().RedeliverAsync("tenant", webhook.Id, redelivery.Id, "dev@example.com");

        var call = Assert.Single(_recorded);
        Assert.Equal(first, call.Redelivery!.RedeliveryOf);
        Assert.Equal("first-request", call.Redelivery.RedeliveryOfRequestId);
        // The stored header is replaced, not sent twice.
        Assert.Equal(first.ToString(), call.Headers[WebhookRedeliveryService.RedeliveryOfHeader]);
    }

    [Theory]
    [InlineData("timeoutSeconds=2", 2)]
    [InlineData("other=1", 60)]               // no timeout in the URL: the webhook's 60 s default
    [InlineData("timeoutSeconds=300", 290)]   // capped, so Node callers get an answer before their 300 s limit
    public async Task TheWaitFollowsTheWebhooksTimeoutUpToTheCap(string timeoutQuery, int expectedSeconds)
    {
        var webhook = GivenWebhook(webhookUrl: $"/api/user/webhooks/builtin?{timeoutQuery}&agentName=agent&workflowName=W&webhookName=GitHub&activationName=activation");
        var original = GivenIncoming(webhook);

        await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Assert.Single(_waits));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ATruncatedCallCannotBeRedelivered(bool bodyTruncated, bool headersTruncated)
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook, i =>
        {
            i.BodyTruncated = bodyTruncated;
            i.HeadersTruncated = headersTruncated;
        });

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task AnotherTenantsWebhookIsNotFound()
    {
        var webhook = GivenWebhook(tenantId: "other-tenant");

        var result = await Service().RedeliverAsync("tenant", webhook.Id, Guid.CreateVersion7(), "dev@example.com");

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task AMissingCallIsNotFound()
    {
        var webhook = GivenWebhook();

        var result = await Service().RedeliverAsync("tenant", webhook.Id, Guid.CreateVersion7(), "dev@example.com");

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example.com/api/user/webhooks/builtin?apikeyId=x")]
    [InlineData("/api/user/webhooks/other?apikeyId=x")]
    [InlineData("/api/user/webhooks/builtin?agentName=agent&workflowName=W")]          // no webhookName
    [InlineData("/api/user/webhooks/builtin?agentName=a:b&workflowName=W&webhookName=GitHub")]
    public async Task AWebhookWithoutAUsableBuiltinUrlCannotBeRedelivered(string? webhookUrl)
    {
        var webhook = GivenWebhook(webhookUrl: webhookUrl);
        var original = GivenIncoming(webhook);

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task ACallCannotBeRedeliveredAgainWhileItsRedeliveryIsRunning()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        var release = new TaskCompletionSource<ConversationMessage?>();
        var started = new TaskCompletionSource();
        _pendingRequests
            .Setup(p => p.WaitForResponseAsync<ConversationMessage>(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), MessageType.Webhook, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                started.TrySetResult();
                return release.Task;
            });

        var first = Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var second = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");
        release.SetResult(null);

        Assert.Equal(StatusCode.Conflict, second.StatusCode);
        Assert.True((await first).IsSuccess);
        // Once the first one is done, the call can be redelivered again.
        Assert.True((await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com")).IsSuccess);
    }

    private WebhookRedeliveryService Service() => new(
        _incomings.Object, _integrations.Object, _webhookIncomingService.Object, _activationValidation.Object,
        _messageService.Object, _pendingRequests.Object, _tenantContext.Object, _auditLog.Object,
        NullLogger<SyncMessageHandler>.Instance);

    private void GivenTheAgentAnswers(WebhookResponse response) =>
        _pendingRequests
            .Setup(p => p.WaitForResponseAsync<ConversationMessage>(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), MessageType.Webhook, It.IsAny<CancellationToken>()))
            .Callback<string, TimeSpan, MessageType, CancellationToken>((_, wait, _, _) => _waits.Add(wait))
            .ReturnsAsync(new ConversationMessage
            {
                ThreadId = "thread-1",
                TenantId = "tenant",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "agent",
                Direction = MessageDirection.Outgoing,
                ParticipantId = "webhook",
                WorkflowId = "workflow",
                WorkflowType = "Integrator Workflow",
                Data = response
            });

    private AppIntegration GivenWebhook(string tenantId = "tenant", string? webhookUrl = WebhookPath)
    {
        var configuration = new Dictionary<string, object> { ["webhookName"] = "GitHub", ["timeoutInSeconds"] = 2 };
        if (webhookUrl != null)
        {
            configuration["webhookUrl"] = webhookUrl;
        }

        var webhook = new AppIntegration
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            PlatformId = "builtin_webhook",
            Name = "GitHub",
            AgentName = "agent",
            ActivationName = "activation",
            WorkflowId = "tenant:agent:Integrator Workflow:activation",
            Configuration = configuration,
            Secrets = new AppIntegrationSecrets(),
            MappingConfig = new AppIntegrationMappingConfig(),
            CreatedBy = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _integrations.Setup(r => r.GetByIdAsync(webhook.Id)).ReturnsAsync(webhook);
        return webhook;
    }

    private WebhookIncoming GivenIncoming(AppIntegration webhook, Action<WebhookIncoming>? change = null)
    {
        var incoming = new WebhookIncoming
        {
            TenantId = "tenant",
            IntegrationId = webhook.Id,
            RequestId = "request-1",
            ContentType = "application/json; charset=utf-8",
            Body = Body,
            WorkflowId = webhook.WorkflowId,
            Headers =
            [
                new() { Name = "X-GitHub-Event", Value = "pull_request" },
                new() { Name = "X-Repeated", Value = "a" },
                new() { Name = "X-Repeated", Value = "b" },
                new() { Name = "Authorization", Value = "Bearer sk-Xnai-original" }
            ]
        };
        change?.Invoke(incoming);
        _incomings.Setup(r => r.GetAsync("tenant", webhook.Id, incoming.Id)).ReturnsAsync(incoming);
        return incoming;
    }
}
