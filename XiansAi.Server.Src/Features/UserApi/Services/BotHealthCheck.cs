
using Features.UserApi.Websocket;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Features.UserApi.Services;

/// <summary>
/// Health check for optimized bot services
/// </summary>
public class BotHealthCheck : IHealthCheck
{
    private readonly IBotService _botService;
    private readonly ILogger<BotHealthCheck> _logger;

    public BotHealthCheck(
        IBotService botService,
        ILogger<BotHealthCheck> logger)
    {
        _botService = botService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get connection metrics
            var metrics = BotHub.GetConnectionMetrics();
            
            // Simple health check - service should be responsive
            var testRequest = new BotRequest
            {
                ParticipantId = "health-check",
                WorkflowType = "health:check",
                Text = "ping"
            };

            // This would normally fail due to validation, but the service should handle it gracefully
            var result = await _botService.ProcessBotRequestAsync(testRequest);
            
            return HealthCheckResult.Healthy("Optimized bot service is healthy", new Dictionary<string, object>
            {
                ["metrics"] = metrics,
                ["timestamp"] = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for optimized bot service");
            return HealthCheckResult.Unhealthy("Optimized bot service is unhealthy", ex);
        }
    }
} 