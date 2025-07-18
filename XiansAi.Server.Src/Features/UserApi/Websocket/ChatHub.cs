using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;

namespace Features.UserApi.Websocket
{
    [Authorize(Policy = "WebsocketAuthPolicy")]
    public class ChatHub : Hub
    {
        private readonly IMessageService _messageService;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<ChatHub> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ChatHub(IMessageService messageService, ITenantContext tenantContext, IHttpContextAccessor httpContextAccessor, ILogger<ChatHub> logger)
        {
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private ITenantContext GetScopedTenantContext()
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext?.RequestServices != null)
                {
                    var scopedTenantContext = httpContext.RequestServices.GetService<ITenantContext>();
                    if (scopedTenantContext != null)
                    {
                        return scopedTenantContext;
                    }
                }
                
                // Fallback to injected tenant context
                _logger.LogWarning("Using fallback tenant context for connection {ConnectionId}", Context.ConnectionId);
                return _tenantContext;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting scoped tenant context, using fallback for connection {ConnectionId}", Context.ConnectionId);
                return _tenantContext;
            }
        }

        private IMessageService GetScopedMessageService()
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext?.RequestServices != null)
                {
                    var scopedMessageService = httpContext.RequestServices.GetService<IMessageService>();
                    if (scopedMessageService != null)
                    {
                        _logger.LogDebug("Using scoped MessageService for connection {ConnectionId}", Context.ConnectionId);
                        return scopedMessageService;
                    }
                }
                
                // Fallback to injected message service
                _logger.LogWarning("Using fallback MessageService for connection {ConnectionId}", Context.ConnectionId);
                return _messageService;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting scoped MessageService, using fallback for connection {ConnectionId}", Context.ConnectionId);
                return _messageService;
            }
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                var tenantContext = GetScopedTenantContext();
                
                if (!IsValidTenantContext(tenantContext))
                {
                    _logger.LogError("TenantContext not properly initialized for connection {ConnectionId}. TenantId: {TenantId}, User: {UserId}", 
                        Context.ConnectionId, tenantContext.TenantId, tenantContext.LoggedInUser);
                    await Clients.Caller.SendAsync("ConnectionError", new
                    {
                        StatusCode = StatusCodes.Status401Unauthorized,
                        Message = "Invalid tenant context"
                    });
                    Context.Abort();
                    return;
                }

                _logger.LogInformation("SignalR Connection established for user: {UserId}, Tenant: {TenantId}, Connection: {ConnectionId}",
                    tenantContext.LoggedInUser, tenantContext.TenantId, Context.ConnectionId);

                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnectedAsync for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("ConnectionError", new
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                    Message = "Internal server error"
                });
                Context.Abort();
            }
        }

        private static bool IsValidTenantContext(ITenantContext tenantContext)
        {
            return !string.IsNullOrEmpty(tenantContext.TenantId) && 
                   !string.IsNullOrEmpty(tenantContext.LoggedInUser);
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger.LogWarning(exception, "SignalR Connection disconnected with exception for connection {ConnectionId}", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("SignalR Connection disconnected normally for connection {ConnectionId}", Context.ConnectionId);
            }
            
            return base.OnDisconnectedAsync(exception);
        }

        public async Task GetThreadHistory(string workflow, string participantId, int page, int pageSize)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(workflow))
            {
                _logger.LogWarning("GetThreadHistory called with null or empty workflow on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Workflow parameter is required");
                return;
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                _logger.LogWarning("GetThreadHistory called with null or empty participantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "ParticipantId parameter is required");
                return;
            }

            if (page < 0)
            {
                _logger.LogWarning("GetThreadHistory called with negative page {Page} on connection {ConnectionId}", page, Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Page must be non-negative");
                return;
            }

            if (pageSize <= 0 || pageSize > 1000)
            {
                _logger.LogWarning("GetThreadHistory called with invalid pageSize {PageSize} on connection {ConnectionId}", pageSize, Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "PageSize must be between 1 and 1000");
                return;
            }

            try
            {
                var tenantContext = GetScopedTenantContext();
                if (!IsValidTenantContext(tenantContext))
                {
                    _logger.LogError("Invalid tenant context for GetThreadHistory call on connection {ConnectionId}. TenantId: {TenantId}, User: {UserId}", 
                        Context.ConnectionId, tenantContext.TenantId, tenantContext.LoggedInUser);
                    await Clients.Caller.SendAsync("Error", "Invalid tenant context");
                    return;
                }

                string? scope = null;
                string workflowId = workflow;
                if(!workflow.StartsWith(tenantContext.TenantId + ":")) {
                    // this is workflowType, convert to workflowId
                    workflowId = tenantContext.TenantId + ":" + workflow;
                }

                var messageService = GetScopedMessageService();
                var result = await messageService.GetThreadHistoryAsync(workflowId, participantId, page, pageSize, scope);
                await Clients.Caller.SendAsync("ThreadHistory", result.Data);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Service configuration error in GetThreadHistory on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Service temporarily unavailable");
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout error in GetThreadHistory on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Request timeout - please try again");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetThreadHistory on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "An unexpected error occurred");
            }
        }

        //TODO: This is a temporary method to send inbound messages to the client. We need to refactor this to use the new messaging endpoints.
        public async Task SendInboundMessage(ChatOrDataRequest request, string messageType)
        {
            // Input validation
            if (request == null)
            {
                _logger.LogWarning("SendInboundMessage called with null request on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Request cannot be null");
                return;
            }

            if (string.IsNullOrWhiteSpace(messageType))
            {
                _logger.LogWarning("SendInboundMessage called with null or empty messageType on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "MessageType is required");
                return;
            }

            _logger.LogDebug("Sending inbound message : {Request}", JsonSerializer.Serialize(request));
            
            try
            {
                var tenantContext = GetScopedTenantContext();
                if (!IsValidTenantContext(tenantContext))
                {
                    _logger.LogError("Invalid tenant context for SendInboundMessage call on connection {ConnectionId}. TenantId: {TenantId}, User: {UserId}", 
                        Context.ConnectionId, tenantContext.TenantId, tenantContext.LoggedInUser);
                    await Clients.Caller.SendAsync("Error", "Invalid tenant context");
                    return;
                }

                // Debug: Log tenant context details
                _logger.LogDebug("SendInboundMessage: Using tenant context - TenantId: {TenantId}, User: {UserId}, Connection: {ConnectionId}",
                    tenantContext.TenantId, tenantContext.LoggedInUser, Context.ConnectionId);

                // Validate message type enum
                if (!Enum.TryParse<MessageType>(messageType, out var messageTypeEnum))
                {
                    _logger.LogWarning("SendInboundMessage called with invalid messageType '{MessageType}' on connection {ConnectionId}", 
                        messageType, Context.ConnectionId);
                    await Clients.Caller.SendAsync("Error", "Invalid message type");
                    return;
                }

                // Ensure request has proper tenant context for downstream services
                if (request.Authorization == null)
                {
                    // Propagate tenant information to the request for downstream services
                    _logger.LogDebug("Adding tenant context to request for downstream services");
                    // Note: We're not modifying the Authorization field directly since that might affect other logic
                    // Instead, we rely on the fact that MessageService should use the scoped ITenantContext
                }

                // Step 1: Process inbound
                var messageService = GetScopedMessageService();
                var inboundResult = await messageService.ProcessIncomingMessage(request, messageTypeEnum);

                if (inboundResult.Data != null)
                {
                    // Notify client message was received
                    await Clients.Caller.SendAsync("InboundProcessed", inboundResult.Data);
                }
                else
                {
                    _logger.LogWarning("ProcessIncomingMessage returned null data with status {StatusCode} on connection {ConnectionId}", 
                        inboundResult.StatusCode, Context.ConnectionId);
                    await Clients.Caller.SendAsync("InboundProcessed", inboundResult.StatusCode);
                    Context.Abort();
                }
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogError(ex, "Configuration error in SendInboundMessage on connection {ConnectionId} - missing required service", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Service configuration error - please try again later");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Service state error in SendInboundMessage on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Service temporarily unavailable");
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout error in SendInboundMessage on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Request timeout - please try again");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid input in SendInboundMessage on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Invalid request data");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SendInboundMessage on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "An unexpected error occurred");
            }
        }

        public async Task SubscribeToAgent(string workflow, string participantId, string tenantId)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(workflow))
            {
                _logger.LogWarning("SubscribeToAgent called with null or empty workflow on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Workflow parameter is required");
                return;
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                _logger.LogWarning("SubscribeToAgent called with null or empty participantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "ParticipantId parameter is required");
                return;
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("SubscribeToAgent called with null or empty tenantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "TenantId parameter is required");
                return;
            }

            try
            {
                var tenantContext = GetScopedTenantContext();
                
                // Security check: ensure user can only subscribe to their own tenant groups
                if (tenantId != tenantContext.TenantId)
                {
                    _logger.LogWarning("SubscribeToAgent: User {UserId} attempted to subscribe to different tenant {RequestedTenantId} vs {ActualTenantId} on connection {ConnectionId}",
                        tenantContext.LoggedInUser, tenantId, tenantContext.TenantId, Context.ConnectionId);
                    await Clients.Caller.SendAsync("Error", "Access denied - invalid tenant");
                    return;
                }

                var workflowId = workflow;
                if (!workflow.StartsWith(tenantContext.TenantId + ":"))
                {
                    workflowId = tenantContext.TenantId + ":" + workflow;
                }
                
                var groupName = workflowId + participantId + tenantId;
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                
                _logger.LogInformation("User {UserId} subscribed to group {GroupName} on connection {ConnectionId}",
                    tenantContext.LoggedInUser, groupName, Context.ConnectionId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Service error in SubscribeToAgent on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Service temporarily unavailable");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SubscribeToAgent on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Failed to subscribe to agent");
            }
        }

        public async Task UnsubscribeFromAgent(string workflow, string participantId, string tenantId)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(workflow))
            {
                _logger.LogWarning("UnsubscribeFromAgent called with null or empty workflow on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Workflow parameter is required");
                return;
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                _logger.LogWarning("UnsubscribeFromAgent called with null or empty participantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "ParticipantId parameter is required");
                return;
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("UnsubscribeFromAgent called with null or empty tenantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "TenantId parameter is required");
                return;
            }

            try
            {
                var tenantContext = GetScopedTenantContext();
                
                // Security check: ensure user can only unsubscribe from their own tenant groups
                if (tenantId != tenantContext.TenantId)
                {
                    _logger.LogWarning("UnsubscribeFromAgent: User {UserId} attempted to unsubscribe from different tenant {RequestedTenantId} vs {ActualTenantId} on connection {ConnectionId}",
                        tenantContext.LoggedInUser, tenantId, tenantContext.TenantId, Context.ConnectionId);
                    await Clients.Caller.SendAsync("Error", "Access denied - invalid tenant");
                    return;
                }

                var workflowId = workflow;
                if (!workflow.StartsWith(tenantContext.TenantId + ":"))
                {
                    workflowId = tenantContext.TenantId + ":" + workflow;
                }

                var groupName = workflowId + participantId + tenantId;
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
                
                _logger.LogInformation("User {UserId} unsubscribed from group {GroupName} on connection {ConnectionId}",
                    tenantContext.LoggedInUser, groupName, Context.ConnectionId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Service error in UnsubscribeFromAgent on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Service temporarily unavailable");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in UnsubscribeFromAgent on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Failed to unsubscribe from agent");
            }
        }
    }
}
