using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Configuration;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

public class WebhookRedeliveryServiceTests
{
    private const string WebhookPath = "/api/user/webhooks/builtin?apikeyId=key-1&timeoutSeconds=2&agentName=agent&workflowName=Integrator%20Workflow&webhookName=GitHub&activationName=activation";
    private const string Body = "{\"action\":\"opened\",\"title\":\"café\"}";

    private readonly Mock<IWebhookIncomingRepository> _incomings = new();
    private readonly Mock<IAppIntegrationRepository> _integrations = new();
    private readonly Mock<IWebhookRedeliverySigner> _signer = new();
    private readonly Mock<IAuditLogService> _auditLog = new();
    private readonly FeatureCollection _serverFeatures = new();
    private readonly StubHandler _handler = new();
    private string? _baseUrl = "http://localhost:5000";
    private int _maxConcurrent = 10;

    private readonly List<object?> _audits = new();

    public WebhookRedeliveryServiceTests()
    {
        _signer.Setup(s => s.Sign(It.IsAny<WebhookRedeliveryClaim>())).Returns("signed-token");
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
    public async Task SendsTheStoredCallToTheWebhookUrlWithTheSignedClaim()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        WebhookRedeliveryClaim? signed = null;
        _signer.Setup(s => s.Sign(It.IsAny<WebhookRedeliveryClaim>()))
            .Callback<WebhookRedeliveryClaim>(c => signed = c)
            .Returns("signed-token");

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("http://localhost:5000" + WebhookPath, sent.Uri.AbsoluteUri);
        Assert.Equal(Encoding.UTF8.GetBytes(Body), sent.Body);
        Assert.Equal("application/json; charset=utf-8", sent.Headers["Content-Type"]);
        Assert.Equal("pull_request", sent.Headers["X-GitHub-Event"]);
        Assert.Equal("sha256=abc", sent.Headers["X-Hub-Signature-256"]);
        Assert.Equal("Bearer secret-key", sent.Headers["Authorization"]);
        Assert.Equal("signed-token", sent.Headers[WebhookRedeliverySigner.TokenHeader]);
        Assert.Equal(original.Id.ToString(), sent.Headers[WebhookRedeliverySigner.RedeliveryOfHeader]);
        Assert.False(sent.Headers.ContainsKey("X-Forwarded-For"));
        Assert.False(sent.Headers.ContainsKey("Host"));
        // The stored Content-Length (999) is not resent; the client computes the real one.
        Assert.False(sent.Headers.ContainsKey("Content-Length"));

        Assert.NotNull(signed);
        Assert.Equal("tenant", signed!.TenantId);
        Assert.Equal(webhook.Id, signed.IntegrationId);
        Assert.Equal(original.Id, signed.RedeliveryOf);
        Assert.Equal("request-1", signed.RedeliveryOfRequestId);
        Assert.Equal("dev@example.com", signed.TriggeredBy);
        Assert.NotEqual(original.Id, signed.DeliveryId);
        Assert.Equal(WebhookRedeliverySigner.HashBody(Body), signed.BodySha256);
    }

    [Fact]
    public async Task ReturnsTheNewDeliveryWhenTheWebhookRecordedIt()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Accepted);
        _signer.Setup(s => s.Sign(It.IsAny<WebhookRedeliveryClaim>()))
            .Callback<WebhookRedeliveryClaim>(c => GivenStored(webhook, c.DeliveryId))
            .Returns("signed-token");

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data!.DeliveryId);
        Assert.Equal(original.Id, result.Data.RedeliveryOf);
        Assert.Equal(202, result.Data.StatusCode);
        var audit = Assert.Single(_audits);
        Assert.Equal(result.Data.DeliveryId.ToString(), Detail(audit, "deliveryId"));
        Assert.Equal(original.Id.ToString(), Detail(audit, "redeliveryOf"));
        Assert.Equal("answered", Detail(audit, "outcome"));
        Assert.Equal(202, Detail(audit, "statusCode"));
    }

    [Fact]
    public async Task ReportsNoDeliveryWhenTheWebhookTurnedTheCallAway()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.DeliveryId);
        Assert.Equal(401, result.Data.StatusCode);
        var audit = Assert.Single(_audits);
        Assert.Null(Detail(audit, "deliveryId"));
        Assert.Equal(401, Detail(audit, "statusCode"));
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
            i.Headers.Add(new WebhookIncomingHeader { Name = WebhookRedeliverySigner.RedeliveryOfHeader, Value = first.ToString() });
        });
        WebhookRedeliveryClaim? signed = null;
        _signer.Setup(s => s.Sign(It.IsAny<WebhookRedeliveryClaim>()))
            .Callback<WebhookRedeliveryClaim>(c => signed = c)
            .Returns("signed-token");

        await Service().RedeliverAsync("tenant", webhook.Id, redelivery.Id, "dev@example.com");

        Assert.Equal(first, signed!.RedeliveryOf);
        Assert.Equal("first-request", signed.RedeliveryOfRequestId);
        // The stored header is replaced, not sent twice.
        Assert.Equal(first.ToString(), Assert.Single(_handler.Requests).Headers[WebhookRedeliverySigner.RedeliveryOfHeader]);
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
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task AnotherTenantsWebhookIsNotFound()
    {
        var webhook = GivenWebhook(tenantId: "other-tenant");

        var result = await Service().RedeliverAsync("tenant", webhook.Id, Guid.CreateVersion7(), "dev@example.com");

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        Assert.Empty(_handler.Requests);
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
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task WithoutAConfiguredBaseUrlTheServersOwnAddressIsUsed()
    {
        _baseUrl = null;
        var addresses = new ServerAddressesFeature();
        addresses.Addresses.Add("https://[::]:5001");
        addresses.Addresses.Add("http://0.0.0.0:5000");
        _serverFeatures.Set<IServerAddressesFeature>(addresses);
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);

        await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal("http://localhost:5000" + WebhookPath, Assert.Single(_handler.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task WithNoAddressToSendToItFails()
    {
        _baseUrl = null;
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task WithoutASigningKeyItFails()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        _signer.Setup(s => s.Sign(It.IsAny<WebhookRedeliveryClaim>())).Throws(new InvalidOperationException("no key"));

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task AnUnreachableWebhookFails()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        _handler.Respond = _ => throw new HttpRequestException("connection refused");

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
        Assert.Equal("unreachable", Detail(Assert.Single(_audits), "outcome"));
    }

    [Fact]
    public async Task AWebhookThatDoesNotAnswerInTimeTimesOut()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        _handler.Respond = _ => throw new TaskCanceledException("timed out");
        _signer.Setup(s => s.Sign(It.IsAny<WebhookRedeliveryClaim>()))
            .Callback<WebhookRedeliveryClaim>(c => GivenStored(webhook, c.DeliveryId))
            .Returns("signed-token");

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.Equal(StatusCode.RequestTimeout, result.StatusCode);
        // The call was recorded before the agent ran, so the audit still names it.
        var audit = Assert.Single(_audits);
        Assert.Equal("timedOut", Detail(audit, "outcome"));
        Assert.NotNull(Detail(audit, "deliveryId"));
    }

    [Fact]
    public async Task WhenTheCallerGoesAwayTheAttemptIsAuditedAndTheCancellationPropagates()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        using var cancelled = new CancellationTokenSource();
        _handler.Respond = _ =>
        {
            cancelled.Cancel();
            throw new TaskCanceledException("caller went away");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com", cancelled.Token));

        Assert.Equal("cancelled", Detail(Assert.Single(_audits), "outcome"));
    }

    [Fact]
    public async Task ACallCannotBeRedeliveredAgainWhileItsRedeliveryIsWaiting()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        using var sent = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _handler.Respond = _ =>
        {
            sent.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var first = Task.Run(() => Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com"));
        Assert.True(sent.Wait(TimeSpan.FromSeconds(10)));

        var second = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");
        release.Set();

        Assert.Equal(StatusCode.Conflict, second.StatusCode);
        Assert.True((await first).IsSuccess);
        // Once the first one is done, the call can be redelivered again.
        Assert.True((await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com")).IsSuccess);
    }

    [Fact]
    public async Task RedeliveriesBeyondTheServersCapAreRefusedWithoutSending()
    {
        _maxConcurrent = 1;
        var webhook = GivenWebhook();
        var busy = GivenIncoming(webhook);
        var other = GivenIncoming(webhook);
        using var sent = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _handler.Respond = _ =>
        {
            sent.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var first = Task.Run(() => Service().RedeliverAsync("tenant", webhook.Id, busy.Id, "dev@example.com"));
        Assert.True(sent.Wait(TimeSpan.FromSeconds(10)));

        var refused = await Service().RedeliverAsync("tenant", webhook.Id, other.Id, "dev@example.com");
        release.Set();

        Assert.Equal(StatusCode.TooManyRequests, refused.StatusCode);
        Assert.Single(_handler.Requests);
        Assert.True((await first).IsSuccess);
        // The slot is freed once the first one is done.
        Assert.True((await Service().RedeliverAsync("tenant", webhook.Id, other.Id, "dev@example.com")).IsSuccess);
    }

    [Fact]
    public async Task TheAnswerIsReadToTheEnd()
    {
        var webhook = GivenWebhook();
        var original = GivenIncoming(webhook);
        var answer = new TrackingStream(Encoding.UTF8.GetBytes("{\"status\":\"ok\"}"));
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(answer) };

        var result = await Service().RedeliverAsync("tenant", webhook.Id, original.Id, "dev@example.com");

        Assert.True(result.IsSuccess);
        Assert.True(answer.ReadToEnd);
    }

    [Theory]
    [InlineData(2, 17)]      // the webhook's wait plus a 15 s margin
    [InlineData(null, 75)]   // no timeout configured: the webhook's 60 s default
    [InlineData(300, 290)]   // capped, so Node callers get an answer before their 300 s limit
    public void TheWaitFollowsTheWebhooksTimeoutUpToTheCap(int? timeoutInSeconds, int expectedSeconds)
    {
        var webhook = GivenWebhook();
        if (timeoutInSeconds == null)
            webhook.Configuration.Remove("timeoutInSeconds");
        else
            webhook.Configuration["timeoutInSeconds"] = timeoutInSeconds.Value;

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), WebhookRedeliveryService.Wait(webhook));
    }

    [Theory]
    [InlineData("http://+:80", "http://localhost/")]
    [InlineData("http://*:8080", "http://localhost:8080/")]
    [InlineData("http://0.0.0.0:5000", "http://localhost:5000/")]
    [InlineData("http://[::]:5000", "http://localhost:5000/")]
    [InlineData("https://127.0.0.1:5001/", "https://127.0.0.1:5001/")]
    [InlineData("http://localhost:5000", "http://localhost:5000/")]
    public void ListeningAddressesWithAWildcardHostPointAtLocalhost(string address, string expected)
    {
        Assert.Equal(expected, WebhookRedeliveryService.ToLoopback(address)?.ToString());
    }

    private WebhookRedeliveryService Service()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(WebhookRedeliveryService.HttpClientName)).Returns(() => new HttpClient(_handler));
        var server = new Mock<IServer>();
        server.SetupGet(s => s.Features).Returns(_serverFeatures);

        return new WebhookRedeliveryService(
            _incomings.Object,
            _integrations.Object,
            _signer.Object,
            factory.Object,
            server.Object,
            _auditLog.Object,
            Options.Create(new WebhookIncomingsOptions { RedeliveryBaseUrl = _baseUrl, MaxConcurrentRedeliveries = _maxConcurrent }),
            NullLogger<WebhookRedeliveryService>.Instance);
    }

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
                new() { Name = "Host", Value = "old.example.com" },
                new() { Name = "Content-Length", Value = "999" },
                new() { Name = "Content-Type", Value = "text/plain" },
                new() { Name = "X-Forwarded-For", Value = "203.0.113.7" },
                new() { Name = "X-GitHub-Event", Value = "pull_request" },
                new() { Name = "X-Hub-Signature-256", Value = "sha256=abc" },
                new() { Name = "Authorization", Value = "Bearer secret-key" }
            ]
        };
        change?.Invoke(incoming);
        _incomings.Setup(r => r.GetAsync("tenant", webhook.Id, incoming.Id)).ReturnsAsync(incoming);
        return incoming;
    }

    private void GivenStored(AppIntegration webhook, Guid id) =>
        _incomings.Setup(r => r.ExistsAsync("tenant", webhook.Id, id)).ReturnsAsync(true);

    private sealed record SentRequest(HttpMethod Method, Uri Uri, byte[] Body, Dictionary<string, string> Headers);

    /// <summary>A response body that notes whether it was read to the end.</summary>
    private sealed class TrackingStream(byte[] data) : MemoryStream(data)
    {
        public bool ReadToEnd { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Track(base.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Track(await base.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Track(await base.ReadAsync(buffer, offset, count, cancellationToken));

        private int Track(int read)
        {
            if (read == 0) ReadToEnd = true;
            return read;
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.Concat(request.Content?.Headers.AsEnumerable() ?? [])
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method, request.RequestUri!, body, headers));
            return Respond(request);
        }
    }
}
