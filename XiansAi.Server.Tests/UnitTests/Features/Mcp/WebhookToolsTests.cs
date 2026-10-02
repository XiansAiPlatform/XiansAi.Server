using Features.Mcp.Tools;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class WebhookToolsTests
{
    private readonly McpTarget _target = new("tenant", "agent", "activation");
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IPermissionsService> _permissions = new();
    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IAppIntegrationService> _integrations = new();

    private WebhookTools Tools() => new(
        _tenant.Object, _permissions.Object, _agents.Object, _activations.Object, _integrations.Object);

    public WebhookToolsTests()
    {
        _tenant.SetupGet(context => context.TenantId).Returns("tenant");
        _tenant.SetupGet(context => context.LoggedInUser).Returns("user");
        _tenant.SetupGet(context => context.UserRoles).Returns([]);
        _permissions.Setup(service => service.HasReadPermission("agent"))
            .ReturnsAsync(ServiceResult<bool>.Success(true));
        _permissions.Setup(service => service.HasWritePermission("agent"))
            .ReturnsAsync(ServiceResult<bool>.Success(true));
        _agents.Setup(repository => repository.GetByNameAsync("agent", "tenant", "user", It.IsAny<string[]>()))
            .ReturnsAsync(new Agent { Id = "agent-id", Name = "agent", Tenant = "tenant", CreatedBy = "user" });
        _activations.Setup(repository => repository.GetByNameAndAgentAsync("tenant", "agent", "activation"))
            .ReturnsAsync(new AgentActivation
            {
                Id = "activation-id", Name = "activation", AgentName = "agent", TenantId = "tenant", CreatedBy = "user"
            });
    }

    [Fact]
    public async Task ListReturnsScopedConfigurationWithoutWebhookUrl()
    {
        _integrations.Setup(service => service.GetBuiltinWebhooksAsync("tenant", "activation", "agent"))
            .ReturnsAsync(ServiceResult<List<AppIntegrationResponse>>.Success([Webhook()]));

        var result = Assert.Single(await Tools().ListWebhooks(_target));

        Assert.Equal("webhook-id", result.Id);
        Assert.Equal("agent:workflow", result.WorkflowName);
        Assert.Equal("IssueCreated", result.WebhookName);
        Assert.Equal(30, result.TimeoutInSeconds);
    }

    [Fact]
    public async Task CreateUsesAuthorizedTargetAndReturnsCredentialUrl()
    {
        _integrations.Setup(service => service.CreateBuiltinWebhookAsync(
                It.IsAny<CreateBuiltinWebhookRequest>(), "tenant", "user"))
            .ReturnsAsync(ServiceResult<AppIntegrationResponse>.Success(Webhook()));

        var result = await Tools().CreateWebhook(
            _target, "agent:workflow", "IssueCreated", "Issues", "participant", 45);

        Assert.Equal("/credential-url", result.WebhookUrl);
        _integrations.Verify(service => service.CreateBuiltinWebhookAsync(
            It.Is<CreateBuiltinWebhookRequest>(request =>
                request.AgentName == "agent" && request.ActivationName == "activation" &&
                request.WorkflowName == "agent:workflow" && request.WebhookName == "IssueCreated" &&
                request.Name == "Issues" && request.ParticipantId == "participant" &&
                request.TimeoutInSeconds == 45), "tenant", "user"), Times.Once);
    }

    [Fact]
    public async Task DeleteRequiresConfirmation()
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteWebhook(_target, "webhook-id"));
        _integrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteRequiresExactWebhookInTargetActivation()
    {
        var other = Webhook();
        other.ActivationName = "other";
        _integrations.Setup(service => service.GetBuiltinWebhooksAsync("tenant", "activation", "agent"))
            .ReturnsAsync(ServiceResult<List<AppIntegrationResponse>>.Success([other]));

        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteWebhook(_target, "webhook-id", true));

        _integrations.Verify(service => service.DeleteBuiltinWebhookAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteUsesExactAuthorizedWebhookId()
    {
        _integrations.Setup(service => service.GetBuiltinWebhooksAsync("tenant", "activation", "agent"))
            .ReturnsAsync(ServiceResult<List<AppIntegrationResponse>>.Success([Webhook()]));
        _integrations.Setup(service => service.DeleteBuiltinWebhookAsync("webhook-id", "tenant"))
            .ReturnsAsync(ServiceResult<bool>.Success(true));

        Assert.True(await Tools().DeleteWebhook(_target, "webhook-id", true));
    }

    private static AppIntegrationResponse Webhook() => new()
    {
        Id = "webhook-id",
        TenantId = "tenant",
        PlatformId = "builtin_webhook",
        Name = "Issues",
        AgentName = "agent",
        ActivationName = "activation",
        WorkflowId = "tenant:agent:agent:workflow:activation",
        WebhookUrl = "/credential-url",
        Configuration = new()
        {
            ["workflowName"] = "agent:workflow",
            ["webhookName"] = "IssueCreated",
            ["participantId"] = "participant",
            ["timeoutInSeconds"] = 30
        },
        IsEnabled = true,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "user"
    };
}
