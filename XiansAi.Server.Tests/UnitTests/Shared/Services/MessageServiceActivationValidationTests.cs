using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Which workflow id an inbound message's activation is validated against. The validator is made to
/// reject, so the message stops right after the check and only the validated id matters.
/// </summary>
public class MessageServiceActivationValidationTests
{
    private const string TenantId = "acme";
    private const string WorkflowType = "My Agent:Integrator Workflow";

    private readonly Mock<IActivationValidationService> _activationValidation = new();
    private readonly MessageService _service;

    public MessageServiceActivationValidationTests()
    {
        _activationValidation
            .Setup(v => v.ValidateWorkflowTargetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(ServiceResult.Failure("Activation is not active", StatusCode.BadRequest));

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(t => t.TenantId).Returns(TenantId);

        _service = new MessageService(
            NullLogger<MessageService>.Instance,
            tenantContext.Object,
            Mock.Of<IConversationRepository>(),
            Mock.Of<IWorkflowSignalService>(),
            Mock.Of<IFeedbackService>(),
            Mock.Of<IMessageFileStorage>(),
            _activationValidation.Object,
            new ConfigurationBuilder().Build(),
            Mock.Of<IIncomingOriginCache>());
    }

    [Fact]
    public async Task WebhookCall_IsValidatedAgainstItsConnectionsWorkflow()
    {
        var result = await _service.ProcessIncomingMessage(new ChatOrDataRequest
        {
            ParticipantId = "webhook",
            RequestId = "req-1",
            WorkflowId = "acme:My Agent:Integrator Workflow:Raven:req-1",
            ActivationWorkflowId = "acme:My Agent:Integrator Workflow:Raven",
        }, MessageType.Webhook);

        Assert.False(result.IsSuccess);
        _activationValidation.Verify(v => v.ValidateWorkflowTargetAsync(
            TenantId, "acme:My Agent:Integrator Workflow:Raven", WorkflowType), Times.Once);
    }

    [Fact]
    public async Task RequestIdNamingTheActivation_DoesNotSkipItsValidation()
    {
        // A caller-chosen request ID equal to the activation must not turn the activation into a suffix.
        await _service.ProcessIncomingMessage(new ChatOrDataRequest
        {
            ParticipantId = "user",
            RequestId = "DeactivatedAct",
            WorkflowId = "acme:My Agent:Integrator Workflow:DeactivatedAct",
        }, MessageType.Chat);

        _activationValidation.Verify(v => v.ValidateWorkflowTargetAsync(
            TenantId, "acme:My Agent:Integrator Workflow:DeactivatedAct", WorkflowType), Times.Once);
    }

    [Fact]
    public void ActivationWorkflowId_IsNotReadFromARequestBody()
    {
        var request = JsonSerializer.Deserialize<ChatOrDataRequest>(
            """{"participantId":"user","workflowId":"acme:My Agent:Integrator Workflow:Off","activationWorkflowId":"acme:My Agent:Integrator Workflow:On"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Null(request!.ActivationWorkflowId);
    }
}
