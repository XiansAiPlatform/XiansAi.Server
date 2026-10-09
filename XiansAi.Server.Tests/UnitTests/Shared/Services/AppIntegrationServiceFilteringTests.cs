using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

public class AppIntegrationServiceFilteringTests
{
    [Fact]
    public async Task GetIntegrationsAppliesPlatformFilterToAgentActivationResults()
    {
        var repository = new Mock<IAppIntegrationRepository>();
        repository.Setup(value => value.GetByAgentActivationAsync(
                "tenant", "agent", "activation", "builtin_webhook"))
            .ReturnsAsync([Integration("builtin_webhook")]);
        var service = new AppIntegrationService(
            repository.Object,
            Mock.Of<IApiKeyService>(),
            Mock.Of<IActivationValidationService>(),
            Mock.Of<IWebhookEventPublisher>(),
            Mock.Of<IAuditLogService>(),
            NullLogger<AppIntegrationService>.Instance);

        var result = await service.GetIntegrationsAsync("tenant", "builtin_webhook", "agent", "activation");

        Assert.True(result.IsSuccess);
        Assert.Equal("builtin_webhook", Assert.Single(result.Data!).PlatformId);
        repository.Verify(value => value.GetByAgentActivationAsync(
            "tenant", "agent", "activation", "builtin_webhook"), Times.Once);
    }

    [Fact]
    public async Task DeleteWebhookUsesTargetedSharedCredentialCheck()
    {
        var repository = new Mock<IAppIntegrationRepository>();
        var integration = Integration("builtin_webhook");
        integration.Configuration["apiKeyId"] = "key-id";
        repository.Setup(value => value.GetByIdAsync(integration.Id)).ReturnsAsync(integration);
        repository.Setup(value => value.HasOtherBuiltinWebhookWithApiKeyAsync("tenant", "key-id", integration.Id))
            .ReturnsAsync(true);
        repository.Setup(value => value.DeleteAsync(integration.Id, "tenant")).ReturnsAsync(true);
        var service = new AppIntegrationService(
            repository.Object,
            Mock.Of<IApiKeyService>(),
            Mock.Of<IActivationValidationService>(),
            Mock.Of<IWebhookEventPublisher>(publisher => publisher.PublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>()) == Task.CompletedTask),
            Mock.Of<IAuditLogService>(),
            NullLogger<AppIntegrationService>.Instance);

        var result = await service.DeleteBuiltinWebhookAsync(integration.Id, "tenant");

        Assert.True(result.IsSuccess);
        repository.Verify(value => value.HasOtherBuiltinWebhookWithApiKeyAsync(
            "tenant", "key-id", integration.Id), Times.Once);
        repository.Verify(value => value.GetByTenantAndPlatformAsync(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteWebhookRevokesUnsharedApiKey()
    {
        var repository = new Mock<IAppIntegrationRepository>();
        var apiKeyService = new Mock<IApiKeyService>();
        var integration = Integration("builtin_webhook");
        integration.Configuration["apiKeyId"] = "key-id";
        repository.Setup(value => value.GetByIdAsync(integration.Id)).ReturnsAsync(integration);
        repository.Setup(value => value.HasOtherBuiltinWebhookWithApiKeyAsync("tenant", "key-id", integration.Id))
            .ReturnsAsync(false);
        repository.Setup(value => value.DeleteAsync(integration.Id, "tenant")).ReturnsAsync(true);
        apiKeyService.Setup(value => value.RevokeApiKeyAsync("key-id", "tenant"))
            .ReturnsAsync(ServiceResult<bool>.Success(true));
        var service = new AppIntegrationService(
            repository.Object,
            apiKeyService.Object,
            Mock.Of<IActivationValidationService>(),
            Mock.Of<IWebhookEventPublisher>(publisher => publisher.PublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>()) == Task.CompletedTask),
            Mock.Of<IAuditLogService>(),
            NullLogger<AppIntegrationService>.Instance);

        var result = await service.DeleteBuiltinWebhookAsync(integration.Id, "tenant");

        Assert.True(result.IsSuccess);
        apiKeyService.Verify(value => value.RevokeApiKeyAsync("key-id", "tenant"), Times.Once);
    }

    private static AppIntegration Integration(string platformId) => new()
    {
        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
        TenantId = "tenant",
        PlatformId = platformId,
        Name = platformId,
        AgentName = "agent",
        ActivationName = "activation",
        WorkflowId = "tenant:agent:workflow:activation",
        Configuration = platformId == "builtin_webhook" ? new() { ["webhookUrl"] = "/webhook" } : [],
        Secrets = new AppIntegrationSecrets { WebhookSecret = "secret" },
        MappingConfig = new AppIntegrationMappingConfig(),
        CreatedBy = "user",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
