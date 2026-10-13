using Shared.Configuration;
using Shared.Data.Models.HealthChecks;
using Shared.Services.HealthChecks;

namespace Tests.UnitTests.Shared.Services.HealthChecks;

public class HealthCheckValidatorTests
{
    private static readonly HealthChecksOptions Options = new();

    private static HealthCheckRequest Chat(Action<HealthCheckRequest>? configure = null)
    {
        var request = new HealthCheckRequest
        {
            Name = "greets user",
            Type = HealthCheckType.Chat,
            IntervalMinutes = 15,
            Chat = new ChatHealthCheckRequest
            {
                Message = "Hello",
                Assertions = [new HealthCheckAssertionRequest { Op = HealthCheckAssertionOp.Contains, Value = "hello" }]
            }
        };
        configure?.Invoke(request);
        return request;
    }

    [Fact]
    public void ValidChatRequest_IsNormalized()
    {
        var (definition, error) = HealthCheckValidator.Validate(Chat(r => r.Name = "  greets user  "), Options);

        Assert.Null(error);
        Assert.NotNull(definition);
        Assert.Equal("greets user", definition!.Name);
        Assert.Equal(HealthCheckLimits.DefaultTimeoutSeconds, definition.TimeoutSeconds);
        Assert.Null(definition.Chat!.WorkflowType);
        Assert.Null(definition.Webhook);
        Assert.Null(definition.Schedule);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(1441)]
    public void IntervalOutsideBounds_IsRejected(int interval)
    {
        var (definition, error) = HealthCheckValidator.Validate(Chat(r => r.IntervalMinutes = interval), Options);

        Assert.Null(definition);
        Assert.Contains("intervalMinutes", error);
    }

    [Fact]
    public void ConfiguredMinimumBelowFive_IsRaisedToFive()
    {
        var lenient = new HealthChecksOptions { MinIntervalMinutes = 1 };

        var (definition, _) = HealthCheckValidator.Validate(Chat(r => r.IntervalMinutes = 3), lenient);

        Assert.Null(definition);
    }

    [Fact]
    public void ConfiguredMinimumAboveFive_IsEnforced()
    {
        var strict = new HealthChecksOptions { MinIntervalMinutes = 30 };

        var (definition, error) = HealthCheckValidator.Validate(Chat(r => r.IntervalMinutes = 15), strict);

        Assert.Null(definition);
        Assert.Contains("between 30", error);
    }

    [Fact]
    public void MissingType_IsRejected()
    {
        var (_, error) = HealthCheckValidator.Validate(Chat(r => r.Type = null), Options);
        Assert.Contains("type is required", error);
    }

    [Fact]
    public void TwoPayloads_AreRejected()
    {
        var request = Chat(r => r.Schedule = new ScheduleHealthCheckRequest { ScheduleId = "s" });

        var (_, error) = HealthCheckValidator.Validate(request, Options);

        Assert.Contains("exactly one", error);
    }

    [Fact]
    public void PayloadNotMatchingType_IsRejected()
    {
        var (_, error) = HealthCheckValidator.Validate(Chat(r => r.Type = HealthCheckType.Webhook), Options);
        Assert.Contains("webhook is required", error);
    }

    [Fact]
    public void ChangingTypeOnUpdate_IsRejected()
    {
        var (_, error) = HealthCheckValidator.Validate(Chat(), Options, existingType: HealthCheckType.Webhook);
        Assert.Contains("type cannot be changed", error);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public void ChatTimeoutOutsideBounds_IsRejected(int timeout)
    {
        var (_, error) = HealthCheckValidator.Validate(Chat(r => r.TimeoutSeconds = timeout), Options);
        Assert.Contains("timeoutSeconds", error);
    }

    [Fact]
    public void TooManyAssertions_AreRejected()
    {
        var request = Chat(r => r.Chat!.Assertions = Enumerable.Range(0, 11)
            .Select(i => new HealthCheckAssertionRequest { Op = HealthCheckAssertionOp.Contains, Value = $"v{i}" })
            .ToList());

        var (_, error) = HealthCheckValidator.Validate(request, Options);

        Assert.Contains("at most 10", error);
    }

    [Fact]
    public void AssertionWithoutOp_IsRejected()
    {
        var request = Chat(r => r.Chat!.Assertions = [new HealthCheckAssertionRequest { Value = "x" }]);

        var (_, error) = HealthCheckValidator.Validate(request, Options);

        Assert.Contains(".op is required", error);
    }

    [Fact]
    public void ChatWithoutAssertions_IsAllowed()
    {
        var (definition, _) = HealthCheckValidator.Validate(Chat(r => r.Chat!.Assertions = null), Options);

        Assert.NotNull(definition);
        Assert.Empty(definition!.Chat!.Assertions);
    }

    [Fact]
    public void WorkflowTypeWithAgentPrefix_IsRejected()
    {
        var (_, error) = HealthCheckValidator.Validate(Chat(r => r.Chat!.WorkflowType = "My Agent:Chat"), Options);
        Assert.Contains("without the agent prefix", error);
    }

    [Fact]
    public void ValidWebhookRequest_KeepsExpectedStatusCode()
    {
        var request = new HealthCheckRequest
        {
            Name = "ticket hook",
            Type = HealthCheckType.Webhook,
            IntervalMinutes = 10,
            TimeoutSeconds = 20,
            Webhook = new WebhookHealthCheckRequest
            {
                WorkflowType = "Inbound",
                MethodName = "ticket-created",
                QueryParams = new() { ["source"] = "health-check" },
                Body = "{\"id\":1}",
                ExpectedStatusCode = 202
            }
        };

        var (definition, error) = HealthCheckValidator.Validate(request, Options);

        Assert.Null(error);
        Assert.Equal(202, definition!.Webhook!.ExpectedStatusCode);
        Assert.Equal("health-check", definition.Webhook.QueryParams["source"]);
        Assert.Equal(20, definition.TimeoutSeconds);
    }

    [Theory]
    [InlineData("apikey")]
    [InlineData("ACCESS_TOKEN")]
    public void WebhookCredentialQueryParam_IsRejected(string key)
    {
        var request = new HealthCheckRequest
        {
            Name = "hook",
            Type = HealthCheckType.Webhook,
            IntervalMinutes = 10,
            Webhook = new WebhookHealthCheckRequest
            {
                WorkflowType = "Inbound",
                MethodName = "ping",
                QueryParams = new() { [key] = "secret" }
            }
        };

        var (_, error) = HealthCheckValidator.Validate(request, Options);

        Assert.Contains("credential", error);
    }

    [Fact]
    public void WebhookInvalidStatusCode_IsRejected()
    {
        var request = new HealthCheckRequest
        {
            Name = "hook",
            Type = HealthCheckType.Webhook,
            IntervalMinutes = 10,
            Webhook = new WebhookHealthCheckRequest { WorkflowType = "Inbound", MethodName = "ping", ExpectedStatusCode = 99 }
        };

        var (_, error) = HealthCheckValidator.Validate(request, Options);

        Assert.Contains("expectedStatusCode", error);
    }

    private static HealthCheckRequest Webhook(Action<WebhookHealthCheckRequest> configure)
    {
        var webhook = new WebhookHealthCheckRequest { WorkflowType = "Inbound", MethodName = "ping" };
        configure(webhook);
        return new HealthCheckRequest { Name = "hook", Type = HealthCheckType.Webhook, IntervalMinutes = 10, TimeoutSeconds = 20, Webhook = webhook };
    }

    [Fact]
    public void WebhookWithoutMode_DefaultsToUpdate()
    {
        var (definition, error) = HealthCheckValidator.Validate(Webhook(_ => { }), Options);

        Assert.Null(error);
        Assert.Equal(WebhookHealthCheckMode.Update, definition!.Webhook!.Mode);
        Assert.Equal("ping", definition.Webhook.MethodName);
    }

    [Fact]
    public void BuiltinWebhook_RequiresWebhookNameAndIgnoresMethod()
    {
        var (missing, error) = HealthCheckValidator.Validate(
            Webhook(w => { w.Mode = WebhookHealthCheckMode.Builtin; w.MethodName = null; }), Options);
        Assert.Null(missing);
        Assert.Contains("webhookName is required", error);

        var (definition, ok) = HealthCheckValidator.Validate(
            Webhook(w => { w.Mode = WebhookHealthCheckMode.Builtin; w.WebhookName = " ticket-created "; }), Options);
        Assert.Null(ok);
        Assert.Equal(WebhookHealthCheckMode.Builtin, definition!.Webhook!.Mode);
        Assert.Equal("ticket-created", definition.Webhook.WebhookName);
        Assert.Equal(string.Empty, definition.Webhook.MethodName);
    }

    [Fact]
    public void BuiltinWebhook_RejectsQueryParams()
    {
        var (_, error) = HealthCheckValidator.Validate(
            Webhook(w => { w.Mode = WebhookHealthCheckMode.Builtin; w.WebhookName = "x"; w.QueryParams = new() { ["a"] = "b" }; }), Options);

        Assert.Contains("not sent to builtin webhooks", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20001)]
    public void MaxResponseMs_MustFitWithinTheTimeout(int maxResponseMs)
    {
        var (_, error) = HealthCheckValidator.Validate(Webhook(w => w.MaxResponseMs = maxResponseMs), Options);
        Assert.Contains("maxResponseMs", error);
    }

    [Fact]
    public void StructuredAssertions_AreValidatedPerOp()
    {
        HealthCheckAssertionRequest A(HealthCheckAssertionOp op, string? path = null, string? value = null) => new() { Op = op, Path = path, Value = value };

        var (definition, error) = HealthCheckValidator.Validate(Webhook(w => w.Assertions =
        [
            A(HealthCheckAssertionOp.JsonEquals, "$.status", "ok"),
            A(HealthCheckAssertionOp.JsonExists, "$.data.items[0]", "ignored"),
            A(HealthCheckAssertionOp.HeaderEquals, "Content-Type", "application/json"),
            A(HealthCheckAssertionOp.HeaderExists, "X-Request-Id"),
        ]), Options);
        Assert.Null(error);
        Assert.Equal(string.Empty, definition!.Webhook!.Assertions[1].Value); // exists ops store no value

        Assert.Contains("path is required", HealthCheckValidator.Validate(Webhook(w => w.Assertions = [A(HealthCheckAssertionOp.JsonExists)]), Options).Error);
        Assert.Contains("not a supported JSON path", HealthCheckValidator.Validate(Webhook(w => w.Assertions = [A(HealthCheckAssertionOp.JsonExists, "$..x")]), Options).Error);
        Assert.Contains("value is required", HealthCheckValidator.Validate(Webhook(w => w.Assertions = [A(HealthCheckAssertionOp.JsonEquals, "$.a")]), Options).Error);
        Assert.Contains("not a valid header name", HealthCheckValidator.Validate(Webhook(w => w.Assertions = [A(HealthCheckAssertionOp.HeaderExists, "Bad Header")]), Options).Error);
    }

    [Fact]
    public void ChatChecks_RejectStructuredAssertions()
    {
        var request = Chat(r => r.Chat!.Assertions = [new HealthCheckAssertionRequest { Op = HealthCheckAssertionOp.JsonEquals, Path = "$.a", Value = "b" }]);

        var (_, error) = HealthCheckValidator.Validate(request, Options);

        Assert.Contains("only supported on webhook checks", error);
    }

    [Fact]
    public void ScheduleRequest_IgnoresTimeout()
    {
        var request = new HealthCheckRequest
        {
            Name = "nightly",
            Type = HealthCheckType.Schedule,
            IntervalMinutes = 60,
            TimeoutSeconds = 9999,
            Schedule = new ScheduleHealthCheckRequest { ScheduleId = " acme:Agent:nightly ", RequireLastRunSuccess = false }
        };

        var (definition, error) = HealthCheckValidator.Validate(request, Options);

        Assert.Null(error);
        Assert.Equal("acme:Agent:nightly", definition!.Schedule!.ScheduleId);
        Assert.False(definition.Schedule.RequireLastRunSuccess);
        Assert.Equal(HealthCheckLimits.DefaultTimeoutSeconds, definition.TimeoutSeconds);
    }

    [Fact]
    public void NameWithMarkup_IsRejected()
    {
        var (_, error) = HealthCheckValidator.Validate(Chat(r => r.Name = "<script>"), Options);
        Assert.Contains("invalid characters", error);
    }
}
