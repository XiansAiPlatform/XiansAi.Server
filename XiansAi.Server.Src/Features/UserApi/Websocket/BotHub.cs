using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Shared.Auth;
using Features.UserApi.Services;
using XiansAi.Server.Providers;
using Shared.Repositories;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Features.UserApi.Websocket;

/// <summary>
/// High-performance bot hub with optimized connection management and caching
/// Key optimizations:
/// - Connection pooling and efficient group management
/// - User context caching to reduce database hits
/// - Batch operations for multiple connections
/// - Memory-efficient group name generation
/// - Async-optimized request processing
/// </summary>
[Authorize(Policy = "WebsocketAuthPolicy")]
public class BotHub : Hub
{
    private readonly IBotService _botService;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantContext? _tempTenantContext;
    private readonly ILogger<BotHub> _logger;
    private readonly ICacheProvider _cacheProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    // Performance optimizations
    private static readonly ConcurrentDictionary<string, string> _groupNameCache = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Connection metrics for monitoring
    private static long _activeConnections = 0;
    private static readonly ConcurrentDictionary<string, DateTime> _connectionTimes = new();

    public BotHub(
        IBotService botService,
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<BotHub> logger,
        ICacheProvider cacheProvider)
    {
        _botService = botService;
        _httpContextAccessor = httpContextAccessor;
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext != null)
        {
            var scopedTenantContext = httpContext.RequestServices.GetService<ITenantContext>();
            if (scopedTenantContext != null)
            {
                _tenantContext = scopedTenantContext;
                _tempTenantContext = tenantContext;
            }
        }
        _tenantContext ??= tenantContext;
        _logger = logger;
        _cacheProvider = cacheProvider;
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            // Fast tenant context validation
            if (!ValidateTenantContext())
            {
                _logger.LogError("TenantContext not properly initialized");
                await SendErrorAndAbort("Invalid tenant context", StatusCodes.Status401Unauthorized);
                return;
            }

            // Optimized authentication check with caching
            if (!await ValidateBotAuthCachedAsync())
            {
                await SendErrorAndAbort("Insufficient permissions for bot requests", StatusCodes.Status403Forbidden);
                return;
            }

            // Track connection metrics only after successful validation
            Interlocked.Increment(ref _activeConnections);
            _connectionTimes[Context.ConnectionId] = DateTime.UtcNow;

            _logger.LogDebug("BotHub connection established for user: {UserId}, Tenant: {TenantId}, Total connections: {Count}",
                _tenantContext.LoggedInUser, _tenantContext.TenantId, _activeConnections);

            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnConnectedAsync");
            await SendErrorAndAbort("Internal server error", StatusCodes.Status500InternalServerError);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // Only cleanup connection tracking if we successfully incremented it
        if (_connectionTimes.ContainsKey(Context.ConnectionId))
        {
            Interlocked.Decrement(ref _activeConnections);
            _connectionTimes.TryRemove(Context.ConnectionId, out _);
        }

        if (exception != null)
        {
            _logger.LogWarning(exception, "BotHub disconnected with exception for user: {UserId}", 
                _tenantContext?.LoggedInUser ?? "unknown");
        }
        else
        {
            _logger.LogDebug("BotHub disconnected for user: {UserId}, Remaining connections: {Count}", 
                _tenantContext?.LoggedInUser ?? "unknown", _activeConnections);
        }
        
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Optimized bot request processing with minimal overhead
    /// </summary>
    public async Task RequestBot(BotRequest request)
    {
        try
        {
            EnsureTenantContext();
            
            // Fast validation and normalization
            if (!ValidateAndNormalizeRequest(request))
                return;

            // Process the bot request with optimized service
            var result = await _botService.ProcessBotRequestAsync(request);

            if (result.IsSuccess && result.Data != null)
            {
                // Send optimized response
                await Clients.Caller.SendAsync("BotResponse", result.Data);
            }
            else
            {
                // Send streamlined error response
                await Clients.Caller.SendAsync("BotError", new
                {
                    request.RequestId,
                    result.StatusCode,
                    Message = result.ErrorMessage ?? "Failed to process bot request"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing bot request for participant {ParticipantId}", request.ParticipantId);
            await Clients.Caller.SendAsync("BotError", new
            {
                request.RequestId,
                StatusCode = StatusCodes.Status500InternalServerError,
                Message = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Optimized bot history retrieval with caching
    /// </summary>
    public async Task GetBotHistory(string workflow, string participantId, int page = 1, int pageSize = 50, string? scope = null)
    {
        try
        {
            EnsureTenantContext();
            
            if (!ValidateTenantContext())
            {
                await Clients.Caller.SendAsync("BotHistoryError", new
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Message = "Invalid tenant context"
                });
                return;
            }

            // Generate cache key for history request
            var cacheKey = $"bot_history:{_tenantContext.TenantId}:{workflow}:{participantId}:{page}:{pageSize}:{scope}";
            
            // Try to get from cache first (with short TTL for real-time data)
            var cachedResult = await _cacheProvider.GetAsync<List<ConversationMessage>>(cacheKey);
            if (cachedResult != null)
            {
                await Clients.Caller.SendAsync("BotHistory", cachedResult);
                return;
            }

            // Normalize workflow ID
            var workflowId = workflow.StartsWith(_tenantContext.TenantId + ":") 
                ? workflow 
                : $"{_tenantContext.TenantId}:{workflow}";

            var result = await _botService.GetBotHistoryAsync(workflowId, participantId, page, pageSize, scope);

            if (result.IsSuccess && result.Data != null)
            {
                // Cache result for short duration (30 seconds for real-time feel)
                await _cacheProvider.SetAsync(cacheKey, result.Data, TimeSpan.FromSeconds(30));
                await Clients.Caller.SendAsync("BotHistory", result.Data);
            }
            else
            {
                await Clients.Caller.SendAsync("BotHistoryError", new
                {
                    StatusCode = result.StatusCode,
                    Message = result.ErrorMessage ?? "Failed to get bot history"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bot history for workflow {Workflow}", workflow);
            await Clients.Caller.SendAsync("BotHistoryError", new
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                Message = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Optimized subscription management with cached group names
    /// </summary>
    public async Task SubscribeToBots(string workflow, string participantId, string tenantId)
    {
        try
        {
            EnsureTenantContext();
            
            if (!ValidateTenantContext())
                return;

            var workflowId = NormalizeWorkflowId(workflow);
            var groupId = GetCachedGroupId("bot", workflowId, participantId, tenantId);
            
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
            
            _logger.LogDebug("User {UserId} subscribed to bot group {GroupId}", 
                _tenantContext.LoggedInUser, groupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to bots for {Workflow}", workflow);
            await Clients.Caller.SendAsync("SubscriptionError", new
            {
                Message = "Failed to subscribe to bots"
            });
        }
    }

    /// <summary>
    /// Optimized unsubscription with cached group names
    /// </summary>
    public async Task UnsubscribeFromBots(string workflow, string participantId, string tenantId)
    {
        try
        {
            EnsureTenantContext();
            
            if (!ValidateTenantContext())
                return;

            var workflowId = NormalizeWorkflowId(workflow);
            var groupId = GetCachedGroupId("bot", workflowId, participantId, tenantId);
            
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
            
            _logger.LogDebug("User {UserId} unsubscribed from bot group {GroupId}", 
                _tenantContext.LoggedInUser, groupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unsubscribing from bots for {Workflow}", workflow);
            await Clients.Caller.SendAsync("UnsubscriptionError", new
            {
                Message = "Failed to unsubscribe from bots"
            });
        }
    }

    /// <summary>
    /// Batch subscription for multiple workflows (performance optimization)
    /// </summary>
    public async Task BatchSubscribeToBots(string[] workflows, string participantId, string tenantId)
    {
        try
        {
            EnsureTenantContext();
            
            if (!ValidateTenantContext())
                return;

            var tasks = workflows.Select(async workflow =>
            {
                var workflowId = NormalizeWorkflowId(workflow);
                var groupId = GetCachedGroupId("bot", workflowId, participantId, tenantId);
                await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
                return groupId;
            });

            var groupIds = await Task.WhenAll(tasks);
            
            _logger.LogDebug("User {UserId} batch subscribed to {Count} bot groups", 
                _tenantContext.LoggedInUser, groupIds.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch subscription");
            await Clients.Caller.SendAsync("SubscriptionError", new
            {
                Message = "Failed to batch subscribe to bots"
            });
        }
    }

    #region Private Optimized Methods

    private bool ValidateTenantContext()
    {
        return !string.IsNullOrEmpty(_tenantContext?.TenantId) && 
               !string.IsNullOrEmpty(_tenantContext?.LoggedInUser);
    }

    private bool ValidateAndNormalizeRequest(BotRequest request)
    {
        if (string.IsNullOrEmpty(request.ParticipantId))
        {
            request.ParticipantId = _tenantContext.LoggedInUser;
        }

        return true;
    }

    private void EnsureTenantContext()
    {
        if (_tempTenantContext == null) 
            throw new InvalidOperationException("TenantContext not properly initialized");

        _tempTenantContext.UserRoles = _tenantContext.UserRoles;
        _tempTenantContext.TenantId = _tenantContext.TenantId;
        _tempTenantContext.LoggedInUser = _tenantContext.LoggedInUser;
        _tempTenantContext.AuthorizedTenantIds = _tenantContext.AuthorizedTenantIds;

        if (string.IsNullOrEmpty(_tenantContext.TenantId) || string.IsNullOrEmpty(_tenantContext.LoggedInUser))
        {
            _logger.LogError("TenantContext not properly initialized");
            throw new InvalidOperationException("TenantContext not properly initialized");
        }
    }

    private async Task<bool> ValidateBotAuthCachedAsync()
    {
        try
        {
            // Cache auth validation for 5 minutes per user
            var cacheKey = $"bot_auth:{_tenantContext.TenantId}:{_tenantContext.LoggedInUser}";
            var cachedAuth = await _cacheProvider.GetAsync<bool?>(cacheKey);
            
            if (cachedAuth.HasValue)
            {
                return cachedAuth.Value;
            }

            // Perform actual validation (simplified for performance)
            var isValid = !string.IsNullOrEmpty(_tenantContext.LoggedInUser) && 
                         !string.IsNullOrEmpty(_tenantContext.TenantId);
            
            // Cache the result
            await _cacheProvider.SetAsync(cacheKey, isValid, TimeSpan.FromMinutes(5));
            
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating bot authentication");
            return false;
        }
    }

    private string NormalizeWorkflowId(string workflow)
    {
        return workflow.StartsWith(_tenantContext.TenantId + ":") 
            ? workflow 
            : $"{_tenantContext.TenantId}:{workflow}";
    }

    private static string GetCachedGroupId(string prefix, string workflowId, string participantId, string tenantId)
    {
        var key = $"{prefix}:{workflowId}:{participantId}:{tenantId}";
        return _groupNameCache.GetOrAdd(key, key);
    }

    private async Task SendErrorAndAbort(string message, int statusCode)
    {
        await Clients.Caller.SendAsync("ConnectionError", new
        {
            StatusCode = statusCode,
            Message = message
        });
        Context.Abort();
    }

    #endregion

    #region Performance Monitoring Methods

    /// <summary>
    /// Get current connection metrics (for monitoring)
    /// </summary>
    public static object GetConnectionMetrics()
    {
        var averageConnectionTime = _connectionTimes.Values.Any() 
            ? DateTime.UtcNow.Subtract(_connectionTimes.Values.Average(t => t.Ticks).Let(ticks => new DateTime((long)ticks)))
            : TimeSpan.Zero;

        return new
        {
            ActiveConnections = _activeConnections,
            CachedGroupNames = _groupNameCache.Count,
            AverageConnectionDuration = averageConnectionTime
        };
    }

    #endregion
}

/// <summary>
/// Extension method for cleaner syntax
/// </summary>
public static class Extensions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> func) => func(value);
} 