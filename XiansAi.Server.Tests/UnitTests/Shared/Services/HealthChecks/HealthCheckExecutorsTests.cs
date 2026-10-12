using System.Net;
using Features.UserApi.Services;
using Features.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models.HealthChecks;
using Shared.Models;
using Shared.Models.Schedule;
using Shared.Repositories;
using Shared.Services;
using Shared.Services.HealthChecks;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services.HealthChecks;

public class HealthCheckExecutorsTests
{
    private const string TenantId = "acme";
    private const string AgentName = "Support Agent";
    private const string ActivationName = "prod";

    private static AgentHealthCheck Check(HealthCheckType type, Action<AgentHealthCheck>? configure = null)
    {
        var check = new AgentHealthCheck
        {
            Id = "66f000000000000000000001",
            TenantId = TenantId,
            ActivationId = "66f000000000000000000002",
            AgentName = AgentName,
            ActivationName = ActivationName,
            Name = "check",
            Type = type,
            IntervalMinutes = 5,
            TimeoutSeconds = 10,
            CreatedBy = "admin"
        };
        configure?.Invoke(check);
        return check;
    }

    private static HealthCheckAssertion Contains(string value) => new() { Op = HealthCheckAssertionOp.Contains, Value = value };

    // ----- Chat -----

    private static (ChatHealthCheckExecutor Executor, Mock<IMessageService> Messages) ChatExecutor(
        string? reply, ServiceResult<string>? processResult = null, ServiceResult? validation = null)
    {
        var validationService = new Mock<IActivationValidationService>();
        validationService
            .Setup(v => v.ResolveConversationalWorkflowAsync(TenantId, AgentName, It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<string>.Success("Chat"));
        validationService
            .Setup(v => v.ValidateActivationAsync(TenantId, AgentName, ActivationName, "Chat"))
            .ReturnsAsync(validation ?? ServiceResult.Success());

        var messages = new Mock<IMessageService>();
        messages
            .Setup(m => m.ProcessIncomingMessage(It.IsAny<ChatOrDataRequest>(), MessageType.Chat))
            .ReturnsAsync(processResult ?? ServiceResult<string>.Success("thread-1"));

        var pending = new Mock<IPendingRequestService>();
        pending
            .Setup(p => p.WaitForResponseAsync<ConversationMessage>(It.IsAny<string>(), It.IsAny<TimeSpan>(), MessageType.Chat, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reply == null ? null : new ConversationMessage
            {
                ThreadId = "thread-1",
                TenantId = TenantId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "agent",
                Direction = MessageDirection.Outgoing,
                ParticipantId = ChatHealthCheckExecutor.ParticipantId,
                WorkflowId = "w",
                WorkflowType = $"{AgentName}:Chat",
                Text = reply
            });

        return (new ChatHealthCheckExecutor(validationService.Object, messages.Object, pending.Object, NullLoggerFactory.Instance), messages);
    }

    private static AgentHealthCheck ChatCheck(params HealthCheckAssertion[] assertions) =>
        Check(HealthCheckType.Chat, c => c.Chat = new ChatHealthCheckConfig { Message = "Hello", Assertions = assertions.ToList() });

    [Fact]
    public async Task Chat_ReplyMatchingAssertions_PassesWithSnippet()
    {
        var (executor, messages) = ChatExecutor("Hello! How can I help?");

        var result = await executor.ExecuteAsync(ChatCheck(Contains("hello")), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Pass, result.Status);
        Assert.Equal("Hello! How can I help?", result.ResponseSnippet);
        Assert.True(Assert.Single(result.AssertionResults!).Passed);
        messages.Verify(m => m.ProcessIncomingMessage(
            It.Is<ChatOrDataRequest>(r =>
                r.Text == "Hello"
                && r.ParticipantId == ChatHealthCheckExecutor.ParticipantId
                && r.WorkflowId == $"{TenantId}:{AgentName}:Chat:{ActivationName}"),
            MessageType.Chat), Times.Once);
    }

    [Fact]
    public async Task Chat_ReplyMissingExpectedText_Fails()
    {
        var (executor, _) = ChatExecutor("I cannot help with that.");

        var result = await executor.ExecuteAsync(ChatCheck(Contains("hello")), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Fail, result.Status);
        Assert.Contains("expected response to contain 'hello'", result.Detail);
        Assert.Equal("I cannot help with that.", result.ResponseSnippet);
    }

    [Fact]
    public async Task Chat_NoReplyWithinTimeout_IsError()
    {
        var (executor, _) = ChatExecutor(reply: null);

        var result = await executor.ExecuteAsync(ChatCheck(Contains("hello")), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Error, result.Status);
        Assert.Contains("408", result.Detail);
    }

    [Fact]
    public async Task Chat_ActivationNotRoutable_IsErrorAndSendsNothing()
    {
        var (executor, messages) = ChatExecutor("Hello", validation: ServiceResult.Failure("deactivated", StatusCode.Conflict));

        var result = await executor.ExecuteAsync(ChatCheck(Contains("hello")), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Error, result.Status);
        messages.Verify(m => m.ProcessIncomingMessage(It.IsAny<ChatOrDataRequest>(), It.IsAny<MessageType>()), Times.Never);
    }

    [Fact]
    public async Task Chat_LongReply_IsTruncatedInSnippet()
    {
        var (executor, _) = ChatExecutor(new string('a', 2000));

        var result = await executor.ExecuteAsync(ChatCheck(), CancellationToken.None);

        Assert.Equal(AgentHealthCheckRun.MaxSnippetLength, result.ResponseSnippet!.Length);
    }

    // ----- Webhook -----

    private static AgentHealthCheck WebhookCheck(int? expectedStatus = null, params HealthCheckAssertion[] assertions) =>
        Check(HealthCheckType.Webhook, c => c.Webhook = new WebhookHealthCheckConfig
        {
            WorkflowType = "Inbound",
            MethodName = "ticket-created",
            QueryParams = new() { ["source"] = "health-check" },
            Body = "{\"id\":1}",
            ExpectedStatusCode = expectedStatus,
            Assertions = assertions.ToList()
        });

    private static Mock<IWebhookReceiverService> WebhookReceiver(ServiceResult<WebhookResponse> result)
    {
        var receiver = new Mock<IWebhookReceiverService>();
        receiver
            .Setup(r => r.ProcessWebhook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return receiver;
    }

    [Fact]
    public async Task Webhook_InvokesInboundPathWithActivationWorkflowIdAndTimeout()
    {
        var receiver = WebhookReceiver(ServiceResult<WebhookResponse>.Success(new WebhookResponse { StatusCode = HttpStatusCode.OK, Content = "ok" }));

        var result = await new WebhookHealthCheckExecutor(receiver.Object).ExecuteAsync(WebhookCheck(200), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Pass, result.Status);
        receiver.Verify(r => r.ProcessWebhook(
            TenantId,
            $"{TenantId}:{AgentName}:Inbound:{ActivationName}",
            "ticket-created",
            It.Is<IDictionary<string, string>>(q => q["source"] == "health-check"),
            "{\"id\":1}",
            TimeSpan.FromSeconds(10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_UnexpectedStatusCode_Fails()
    {
        var receiver = WebhookReceiver(ServiceResult<WebhookResponse>.Success(new WebhookResponse { StatusCode = HttpStatusCode.InternalServerError, Content = "boom" }));

        var result = await new WebhookHealthCheckExecutor(receiver.Object).ExecuteAsync(WebhookCheck(200), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Fail, result.Status);
        Assert.Contains("Expected status code 200 but got 500", result.Detail);
    }

    [Fact]
    public async Task Webhook_ContentAssertionFailure_Fails()
    {
        var receiver = WebhookReceiver(ServiceResult<WebhookResponse>.Success(new WebhookResponse { StatusCode = HttpStatusCode.OK, Content = "{\"status\":\"error\"}" }));

        var result = await new WebhookHealthCheckExecutor(receiver.Object)
            .ExecuteAsync(WebhookCheck(null, new HealthCheckAssertion { Op = HealthCheckAssertionOp.NotContains, Value = "error" }), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Fail, result.Status);
    }

    [Fact]
    public async Task Webhook_Timeout_IsError()
    {
        var receiver = WebhookReceiver(ServiceResult<WebhookResponse>.RequestTimeout("no workers"));

        var result = await new WebhookHealthCheckExecutor(receiver.Object).ExecuteAsync(WebhookCheck(), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Error, result.Status);
        Assert.Contains("408", result.Detail);
    }

    // ----- Schedule -----

    private static AgentHealthCheck ScheduleCheck(bool requireLastRunSuccess = true) =>
        Check(HealthCheckType.Schedule, c => c.Schedule = new ScheduleHealthCheckConfig
        {
            ScheduleId = "acme:nightly",
            RequireLastRunSuccess = requireLastRunSuccess
        });

    private static Mock<IScheduleService> Schedules(
        ServiceResult<ScheduleModel> schedule, List<ScheduleRunModel>? history = null)
    {
        var service = new Mock<IScheduleService>();
        service.Setup(s => s.GetScheduleByIdAsync("acme:nightly")).ReturnsAsync(schedule);
        service.Setup(s => s.GetScheduleHistoryAsync("acme:nightly", 1))
            .ReturnsAsync(ServiceResult<List<ScheduleRunModel>>.Success(history ?? new()));
        return service;
    }

    private static ScheduleModel Schedule(ScheduleStatus status = ScheduleStatus.Running, string agentName = AgentName) => new()
    {
        Id = "acme:nightly",
        AgentName = agentName,
        WorkflowType = $"{agentName}:Nightly",
        ScheduleSpec = "0 0 * * *",
        Status = status,
        NextRunTime = DateTime.UtcNow.AddHours(1)
    };

    private static ScheduleRunModel Run(ScheduleRunStatus status) => new()
    {
        RunId = "r1",
        ScheduleId = "acme:nightly",
        ScheduledTime = DateTime.UtcNow.AddHours(-1),
        Status = status,
        ErrorMessage = status == ScheduleRunStatus.Failed ? "activity crashed" : null
    };

    [Fact]
    public async Task Schedule_EnabledWithSuccessfulLastRun_Passes()
    {
        var service = Schedules(ServiceResult<ScheduleModel>.Success(Schedule()), [Run(ScheduleRunStatus.Completed)]);

        var result = await new ScheduleHealthCheckExecutor(service.Object).ExecuteAsync(ScheduleCheck(), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task Schedule_Missing_Fails()
    {
        var service = Schedules(ServiceResult<ScheduleModel>.NotFound("not found"));

        var result = await new ScheduleHealthCheckExecutor(service.Object).ExecuteAsync(ScheduleCheck(), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Fail, result.Status);
        Assert.Contains("does not exist", result.Detail);
    }

    [Fact]
    public async Task Schedule_BelongingToAnotherAgent_IsReportedMissing()
    {
        var service = Schedules(ServiceResult<ScheduleModel>.Success(Schedule(agentName: "Other Agent")));

        var result = await new ScheduleHealthCheckExecutor(service.Object).ExecuteAsync(ScheduleCheck(), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Fail, result.Status);
        Assert.Contains("does not exist", result.Detail);
    }

    [Fact]
    public async Task Schedule_Paused_Fails()
    {
        var service = Schedules(ServiceResult<ScheduleModel>.Success(Schedule(ScheduleStatus.Suspended)));

        var result = await new ScheduleHealthCheckExecutor(service.Object).ExecuteAsync(ScheduleCheck(), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Fail, result.Status);
        Assert.Contains("paused", result.Detail);
    }

    [Fact]
    public async Task Schedule_LastRunFailed_FailsOnlyWhenRequired()
    {
        var service = Schedules(ServiceResult<ScheduleModel>.Success(Schedule()), [Run(ScheduleRunStatus.Failed)]);
        var executor = new ScheduleHealthCheckExecutor(service.Object);

        var strict = await executor.ExecuteAsync(ScheduleCheck(requireLastRunSuccess: true), CancellationToken.None);
        var lenient = await executor.ExecuteAsync(ScheduleCheck(requireLastRunSuccess: false), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Fail, strict.Status);
        Assert.Contains("activity crashed", strict.Detail);
        Assert.Equal(HealthCheckStatus.Pass, lenient.Status);
    }

    [Fact]
    public async Task Schedule_NoRunsYet_Passes()
    {
        var service = Schedules(ServiceResult<ScheduleModel>.Success(Schedule()));

        var result = await new ScheduleHealthCheckExecutor(service.Object).ExecuteAsync(ScheduleCheck(), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Pass, result.Status);
        Assert.Contains("no runs yet", result.Detail);
    }

    [Fact]
    public async Task Schedule_ReadFailure_IsError()
    {
        var service = Schedules(ServiceResult<ScheduleModel>.InternalServerError("temporal down"));

        var result = await new ScheduleHealthCheckExecutor(service.Object).ExecuteAsync(ScheduleCheck(), CancellationToken.None);

        Assert.Equal(HealthCheckStatus.Error, result.Status);
    }
}
