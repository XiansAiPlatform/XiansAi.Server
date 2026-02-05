using Features.AppsApi.Models;
using Features.AppsApi.Repositories;
using Features.AppsApi.Handlers;
using Features.UserApi.Services;
using Shared.Repositories;
using Shared.Auth;

namespace Features.AppsApi.Services;

/// <summary>
/// Background service that routes outgoing messages from agents to external platforms (Slack, Teams, etc.)
/// Subscribes to message events and delivers them to the appropriate platform via webhook handlers.
/// </summary>
public class AppMessageRouterService : BackgroundService
{
    private readonly ILogger<AppMessageRouterService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageEventPublisher _messageEventPublisher;

    public AppMessageRouterService(
        ILogger<AppMessageRouterService> logger,
        IServiceScopeFactory scopeFactory,
        IMessageEventPublisher messageEventPublisher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _messageEventPublisher = messageEventPublisher ?? throw new ArgumentNullException(nameof(messageEventPublisher));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("App Message Router Service started - listening for outgoing messages");

        try
        {
            // Subscribe to message events
            _messageEventPublisher.MessageReceived += async (messageEvent) =>
            {
                await RouteMessageAsync(messageEvent, stoppingToken);
            };

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("App Message Router Service is stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in App Message Router Service");
            throw;
        }
    }

    private async Task RouteMessageAsync(MessageStreamEvent messageEvent, CancellationToken cancellationToken)
    {
        try
        {
            var message = messageEvent.Message;

            // Only process outgoing messages
            if (message.Direction != MessageDirection.Outgoing)
            {
                return;
            }

            // Check if message is from an app integration (origin format: "app:{platformId}:{integrationId}")
            if (string.IsNullOrEmpty(message.Origin) || !message.Origin.StartsWith("app:"))
            {
                return; // Not an app-routed message
            }

            var originParts = message.Origin.Split(':');
            if (originParts.Length != 3)
            {
                _logger.LogWarning("Invalid app origin format: {Origin}", message.Origin);
                return;
            }

            var platformId = originParts[1];
            var integrationId = originParts[2];

            _logger.LogInformation("Routing outgoing message to {Platform} integration {IntegrationId}",
                platformId, integrationId);

            // Create a scope to resolve scoped services
            using var scope = _scopeFactory.CreateScope();
            var appIntegrationRepository = scope.ServiceProvider.GetRequiredService<IAppIntegrationRepository>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            // Load integration
            var integration = await appIntegrationRepository.GetByIdAsync(integrationId);
            if (integration == null)
            {
                _logger.LogWarning("Integration {IntegrationId} not found", integrationId);
                return;
            }

            if (!integration.IsEnabled)
            {
                _logger.LogWarning("Integration {IntegrationId} is disabled", integrationId);
                return;
            }

            // Set tenant context from integration
            tenantContext.TenantId = integration.TenantId;
            tenantContext.LoggedInUser = "app:router";

            // Route to platform-specific handler
            await RouteByPlatformAsync(platformId, integration, message, scope, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing outgoing message");
        }
    }

    private async Task RouteByPlatformAsync(
        string platformId,
        AppIntegration integration,
        ConversationMessage message,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        switch (platformId.ToLowerInvariant())
        {
            case "slack":
                var slackHandler = scope.ServiceProvider.GetRequiredService<ISlackWebhookHandler>();
                await slackHandler.SendMessageToSlackAsync(integration, message, cancellationToken);
                break;

            case "msteams":
                var teamsHandler = scope.ServiceProvider.GetRequiredService<ITeamsWebhookHandler>();
                await teamsHandler.SendMessageToTeamsAsync(integration, message, cancellationToken);
                break;

            case "outlook":
                _logger.LogInformation("Outlook outbound routing not yet implemented");
                // TODO: Implement Outlook handler
                break;

            default:
                _logger.LogWarning("Unsupported platform for outbound routing: {Platform}", platformId);
                break;
        }
    }
}
