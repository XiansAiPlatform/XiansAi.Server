using Moq;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Data.Models;
using Microsoft.Extensions.Logging;

namespace Tests.UnitTests.Shared.Services;

public class MessageServiceTests
{
    [Fact]
    public async Task ProcessIncomingMessage_ReturnsForbidden_WhenTokenLimitExceeded()
    {
        // Arrange
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupAllProperties();
        tenantContext.Object.TenantId = "tenant-a";
        tenantContext.Object.LoggedInUser = "user-a";

        var conversationRepo = new Mock<IConversationRepository>();
        var workflowSignalService = new Mock<IWorkflowSignalService>();
        var tokenUsageService = new Mock<ITokenUsageService>();
        var logger = new Mock<ILogger<MessageService>>();

        tokenUsageService.Setup(s => s.CheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenUsageStatus(
                Enabled: true,
                MaxTokens: 100,
                WindowSeconds: 60,
                TokensUsed: 100,
                TokensRemaining: 0,
                WindowStart: DateTime.UtcNow.AddMinutes(-1),
                WindowEndsAt: DateTime.UtcNow.AddMinutes(1),
                IsExceeded: true));

        var service = new MessageService(
            logger.Object,
            tenantContext.Object,
            conversationRepo.Object,
            workflowSignalService.Object,
            tokenUsageService.Object);

        var request = new ChatOrDataRequest
        {
            ParticipantId = "participant-1",
            WorkflowType = "agent:flow",
            Authorization = "token"
        };

        // Act
        var result = await service.ProcessIncomingMessage(request, MessageType.Chat);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Forbidden, result.StatusCode);
        tokenUsageService.Verify(s => s.CheckAsync("tenant-a", "participant-1", It.IsAny<CancellationToken>()), Times.Once);
        conversationRepo.VerifyNoOtherCalls();
        workflowSignalService.VerifyNoOtherCalls();
    }
}

