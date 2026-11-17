using Microsoft.AspNetCore.Http;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

public class WebhookRedeliveryServiceTests
{
    private const string WebhookPath = "/api/user/webhooks/builtin?apikeyId=key-1&timeoutSeconds=2&agentName=agent&workflowName=Integrator%20Workflow&webhookName=GitHub&activationName=activation&scope=ci";
    private const string Body = "{\"action\":\"opened\",\"title\":\"café\"}";

    private readonly Mock<IWebhookIncomingRepository> _incomings = new();
    private readonly Mock<IAppIntegrationRepository> _integrations = new();
    private readonly Mock<IBuiltinWebhookProcessor> _processor = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IAuditLogService> _auditLog = new();
    private readonly List<BuiltinWebhookCallRequest> _sent = new();
    private readonly List<object?> _audits = new();

    public WebhookRedeliveryServiceTests()
    {
        _tenantContext.SetupProperty(t => t.Authorization, "admin-token");
        GivenTheWebhookAnswers(_ => new BuiltinWebhookOutcome(Guid.CreateVersion7(), null, null, new { }));
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
    public async Task RunsTheStoredCallThroughTheWebhookWithTheValuesFromItsUrl()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.True(result.IsSuccess);
        var call = Assert.Single(_sent);
        Assert.Equal("tenant", call.TenantId);
        Assert.Equal("agent", call.AgentName);
        Assert.Equal("Integrator Workflow", call.WorkflowName);
        Assert.Equal("GitHub", call.WebhookName);
        Assert.Equal("activation", call.ActivationName);
        Assert.Equal("key-1", call.ApiKeyId);
        Assert.Equal(2, call.TimeoutSeconds);
        Assert.Equal("ci", call.Scope);
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
        Assert.Null(_tenantContext.Object.Authorization);
    }

    [Fact]
    public async Task ACallThatDidNotAuthenticateWithAnApiKeyHeaderPassesNoCredential()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook, i => i.Headers.RemoveAll(h => h.Name == "Authorization"));

        await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Null(Assert.Single(_sent).Authorization);
        Assert.Null(_tenantContext.Object.Authorization);
    }

    [Fact]
    public async Task ReturnsTheNewDeliveryAndWhatTheWebhookAnswered()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        var deliveryId = Guid.CreateVersion7();
        GivenTheWebhookAnswers(_ => new BuiltinWebhookOutcome(
            deliveryId, null, new global::Shared.Models.WebhookResponse(System.Net.HttpStatusCode.Accepted), null));

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(deliveryId, result.Data!.DeliveryId);
        Assert.Equal(original.Id, result.Data.RedeliveryOf);
        Assert.Equal(202, result.Data.StatusCode);
        var audit = Assert.Single(_audits);
        Assert.Equal(deliveryId.ToString(), Detail(audit, "deliveryId"));
        Assert.Equal(original.Id.ToString(), Detail(audit, "redeliveryOf"));
        Assert.Equal(202, Detail(audit, "statusCode"));
    }

    [Fact]
    public async Task ReportsNoDeliveryWhenTheWebhookTurnedTheCallAway()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        GivenTheWebhookAnswers(_ => new BuiltinWebhookOutcome(
            null, Results.Json(new { error = "Activation is not active" }, statusCode: 409), null, null));

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.DeliveryId);
        Assert.Equal(409, result.Data.StatusCode);
        Assert.Null(Detail(Assert.Single(_audits), "deliveryId"));
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

        var call = Assert.Single(_sent);
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

        Assert.Equal(expectedSeconds, Assert.Single(_sent).TimeoutSeconds);
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
    public async Task AWebhookWithoutABuiltinUrlCannotBeRedelivered(string? webhookUrl)
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
        var release = new TaskCompletionSource<BuiltinWebhookOutcome>();
        var started = new TaskCompletionSource();
        _processor
            .Setup(p => p.ProcessAsync(It.IsAny<BuiltinWebhookCallRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                started.TrySetResult();
                return release.Task;
            });

        var first = Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var second = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");
        release.SetResult(new BuiltinWebhookOutcome(null, null, null, new { }));

        Assert.Equal(StatusCode.Conflict, second.StatusCode);
        Assert.True((await first).IsSuccess);
        // Once the first one is done, the call can be redelivered again.
        Assert.True((await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com")).IsSuccess);
    }

    private WebhookRedeliveryService Service() => new(
        _incomings.Object, _integrations.Object, _processor.Object, _tenantContext.Object, _auditLog.Object);

    private void GivenTheWebhookAnswers(Func<BuiltinWebhookCallRequest, BuiltinWebhookOutcome> answer) =>
        _processor
            .Setup(p => p.ProcessAsync(It.IsAny<BuiltinWebhookCallRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BuiltinWebhookCallRequest, CancellationToken>((call, _) => _sent.Add(call))
            .ReturnsAsync((BuiltinWebhookCallRequest call, CancellationToken _) => answer(call));

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
