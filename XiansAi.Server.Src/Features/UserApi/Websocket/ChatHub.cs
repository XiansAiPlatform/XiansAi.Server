using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Temporal;

namespace Features.UserApi.Websocket
{
    [Authorize(Policy = "WebsocketAuthPolicy")]
    public class ChatHub : Hub
    {
        // SignalR method names
        private static class SignalRMethods
        {
            public const string Error = "Error";
            public const string ConnectionError = "ConnectionError";
            public const string ThreadHistory = "ThreadHistory";
            public const string InboundProcessed = "InboundProcessed";
        }

        // Error messages
        private static class ErrorMessages
        {
            public const string WorkflowRequired = "Workflow parameter is required";
            public const string ParticipantIdRequired = "ParticipantId parameter is required";
            public const string TenantIdRequired = "TenantId parameter is required";
            public const string RequestCannotBeNull = "Request cannot be null";
            public const string MessageTypeRequired = "MessageType is required";
            public const string InvalidTenantContext = "Invalid tenant context";
            public const string InvalidMessageType = "Invalid message type. Valid types are: Chat, Data";
            public const string PageMustBeNonNegative = "Page must be non-negative";
            public const string InvalidPageSize = "PageSize must be between 1 and 1000";
            public const string AccessDeniedInvalidTenant = "Access denied - invalid tenant";
            public const string ServiceTemporarilyUnavailable = "Service temporarily unavailable";
            public const string RequestTimeout = "Request timeout - please try again";
            public const string InvalidRequestData = "Invalid request data";
            public const string UnexpectedError = "An unexpected error occurred";
            public const string ServiceConfigurationError = "Service configuration error - please try again later";
            public const string FailedToSubscribe = "Failed to subscribe to agent";
            public const string FailedToUnsubscribe = "Failed to unsubscribe from agent";
            public const string InternalServerError = "Internal server error";
        }

        // Validation constants
        private static class ValidationLimits
        {
            public const int MaxPageSize = 1000;
            public const int MinPageSize = 1;
            public const int MinPage = 0;
        }

        private readonly ILogger<ChatHub> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ChatHub(IHttpContextAccessor httpContextAccessor, ILogger<ChatHub> logger)
        {
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
                
                throw new InvalidOperationException("TenantContext not properly initialized");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting scoped tenant context, using fallback for connection {ConnectionId}", Context.ConnectionId);
                throw new InvalidOperationException("TenantContext not properly initialized");
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
                throw new InvalidOperationException("MessageService not properly initialized");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting scoped MessageService, using fallback for connection {ConnectionId}", Context.ConnectionId);
                throw new InvalidOperationException("MessageService not properly initialized");
            }
        }

        public override async Task OnConnectedAsync()
        {
            var cancellationToken = Context.ConnectionAborted;
            
            try
            {
                var tenantContext = GetScopedTenantContext();
                
                if (!IsValidTenantContext(tenantContext))
                {
                    _logger.LogError("TenantContext not properly initialized for connection {ConnectionId}. TenantId: {TenantId}, User: {UserId}", 
                        Context.ConnectionId, tenantContext.TenantId, tenantContext.LoggedInUser);
                    await Clients.Caller.SendAsync(SignalRMethods.ConnectionError, new
                    {
                        StatusCode = StatusCodes.Status401Unauthorized,
                        Message = ErrorMessages.InvalidTenantContext
                    }, cancellationToken);
                    Context.Abort();
                    return;
                }

                _logger.LogInformation("SignalR Connection established for user: {UserId}, Tenant: {TenantId}, Connection: {ConnectionId}",
                    tenantContext.LoggedInUser, tenantContext.TenantId, Context.ConnectionId);

                await base.OnConnectedAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("OnConnectedAsync cancelled for connection {ConnectionId}", Context.ConnectionId);
                // Don't send response for cancelled operations
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnectedAsync for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.ConnectionError, new
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                    Message = ErrorMessages.InternalServerError
                }, cancellationToken);
                Context.Abort();
            }
        }

        private void EnsureTenantContext()
        {
            if (_tempTenantContext == null) throw new InvalidOperationException("TenantContext not properly initialized");

            _tempTenantContext.CopyFrom(_tenantContext);

            if (string.IsNullOrEmpty(_tenantContext.TenantId) || string.IsNullOrEmpty(_tenantContext.LoggedInUser))
            {
                _logger.LogError("TenantContext not properly initialized");
                throw new InvalidOperationException("TenantContext not properly initialized");
            }
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

        public async Task GetThreadHistory(string workflow, string participantId, int page, int pageSize){
            await GetScopedThreadHistory(workflow, participantId, page, pageSize, null);
        }

        public async Task GetScopedThreadHistory(string workflow, string participantId, int page, int pageSize, string? scope)
        {
            var cancellationToken = Context.ConnectionAborted;
            
            // Input validation
            if (string.IsNullOrWhiteSpace(workflow))
            {
                _logger.LogWarning("GetThreadHistory called with null or empty workflow on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.WorkflowRequired, cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                _logger.LogWarning("GetThreadHistory called with null or empty participantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.ParticipantIdRequired, cancellationToken);
                return;
            }

            if (page < ValidationLimits.MinPage)
            {
                _logger.LogWarning("GetThreadHistory called with negative page {Page} on connection {ConnectionId}", page, Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.PageMustBeNonNegative, cancellationToken);
                return;
            }

            if (pageSize <= ValidationLimits.MinPageSize || pageSize > ValidationLimits.MaxPageSize)
            {
                _logger.LogWarning("GetThreadHistory called with invalid pageSize {PageSize} on connection {ConnectionId}", pageSize, Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.InvalidPageSize, cancellationToken);
                return;
            }

            try
            {
                var tenantContext = GetScopedTenantContext();
                if (!IsValidTenantContext(tenantContext))
                {
                    _logger.LogError("Invalid tenant context for GetThreadHistory call on connection {ConnectionId}. TenantId: {TenantId}, User: {UserId}", 
                        Context.ConnectionId, tenantContext.TenantId, tenantContext.LoggedInUser);
                    await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.InvalidTenantContext, cancellationToken);
                    return;
                }

                string workflowId = workflow;
                if(!workflow.StartsWith(tenantContext.TenantId + ":")) {
                    // this is workflowType, convert to workflowId
                    workflowId = tenantContext.TenantId + ":" + workflow;
                }

                var messageService = GetScopedMessageService();
                var result = await messageService.GetThreadHistoryAsync(workflowId, participantId, page, pageSize, scope);
                await Clients.Caller.SendAsync(SignalRMethods.ThreadHistory, result.Data, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("GetThreadHistory cancelled for connection {ConnectionId}", Context.ConnectionId);
                // Don't send response for cancelled operations
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Service configuration error in GetThreadHistory on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.ServiceTemporarilyUnavailable, cancellationToken);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout error in GetThreadHistory on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.RequestTimeout, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetThreadHistory on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.UnexpectedError, cancellationToken);
            }
        }

        public async Task SendInboundMessage(ChatOrDataRequest request, string messageType)
        {
            var cancellationToken = Context.ConnectionAborted;
            
            // Input validation - check for null request first
            if (request == null)
            {
                _logger.LogWarning("SendInboundMessage called with null request on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.RequestCannotBeNull, cancellationToken);
                return;
            }

            if (request.Workflow != null)
            {
                var identifier = new WorkflowIdentifier(request.Workflow, GetScopedTenantContext());
                request.WorkflowId = identifier.WorkflowId;
                request.WorkflowType = identifier.WorkflowType;
                request.Agent = identifier.AgentName;
            }

            if (string.IsNullOrWhiteSpace(messageType))
            {
                _logger.LogWarning("SendInboundMessage called with null or empty messageType on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.MessageTypeRequired, cancellationToken);
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
                    await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.InvalidTenantContext, cancellationToken);
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
                    await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.InvalidMessageType, cancellationToken);
                    return;
                }

                request.Type = messageTypeEnum;

                // Step 1: Process inbound
                var messageService = GetScopedMessageService();
                var inboundResult = await messageService.ProcessIncomingMessage(request, messageTypeEnum);

                if (inboundResult.Data != null)
                {
                    // Notify client message was received
                    await Clients.Caller.SendAsync(SignalRMethods.InboundProcessed, inboundResult.Data, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("ProcessIncomingMessage returned null data with status {StatusCode} on connection {ConnectionId}", 
                        inboundResult.StatusCode, Context.ConnectionId);
                    await Clients.Caller.SendAsync(SignalRMethods.InboundProcessed, inboundResult.StatusCode, cancellationToken);
                    Context.Abort();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SendInboundMessage cancelled for connection {ConnectionId}", Context.ConnectionId);
                // Don't send response for cancelled operations
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogError(ex, "Configuration error in SendInboundMessage on connection {ConnectionId} - missing required service", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.ServiceConfigurationError, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Service state error in SendInboundMessage on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.ServiceTemporarilyUnavailable, cancellationToken);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout error in SendInboundMessage on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.RequestTimeout, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid input in SendInboundMessage on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.InvalidRequestData, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SendInboundMessage on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.UnexpectedError, cancellationToken);
            }
        }

        public async Task SubscribeToAgent(string workflow, string participantId, string tenantId)
        {
            var cancellationToken = Context.ConnectionAborted;
            
            // Input validation
            if (string.IsNullOrWhiteSpace(workflow))
            {
                _logger.LogWarning("SubscribeToAgent called with null or empty workflow on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.WorkflowRequired, cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                _logger.LogWarning("SubscribeToAgent called with null or empty participantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.ParticipantIdRequired, cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("SubscribeToAgent called with null or empty tenantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.TenantIdRequired, cancellationToken);
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
                    await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.AccessDeniedInvalidTenant, cancellationToken);
                    return;
                }

                var workflowId = workflow;
                if (!workflow.StartsWith(tenantContext.TenantId + ":"))
                {
                    workflowId = tenantContext.TenantId + ":" + workflow;
                }
                
                var groupName = workflowId + participantId + tenantId;
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName, cancellationToken);
                
                _logger.LogInformation("User {UserId} subscribed to group {GroupName} on connection {ConnectionId}",
                    tenantContext.LoggedInUser, groupName, Context.ConnectionId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SubscribeToAgent cancelled for connection {ConnectionId}", Context.ConnectionId);
                // Don't send response for cancelled operations
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Service error in SubscribeToAgent on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.ServiceTemporarilyUnavailable, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SubscribeToAgent on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.FailedToSubscribe, cancellationToken);
            }
        }

        public async Task UnsubscribeFromAgent(string workflow, string participantId, string tenantId)
        {
            var cancellationToken = Context.ConnectionAborted;
            
            // Input validation
            if (string.IsNullOrWhiteSpace(workflow))
            {
                _logger.LogWarning("UnsubscribeFromAgent called with null or empty workflow on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.WorkflowRequired, cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                _logger.LogWarning("UnsubscribeFromAgent called with null or empty participantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.ParticipantIdRequired, cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("UnsubscribeFromAgent called with null or empty tenantId on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.TenantIdRequired, cancellationToken);
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
                    await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.AccessDeniedInvalidTenant, cancellationToken);
                    return;
                }

                var workflowId = workflow;
                if (!workflow.StartsWith(tenantContext.TenantId + ":"))
                {
                    workflowId = tenantContext.TenantId + ":" + workflow;
                }

                var groupName = workflowId + participantId + tenantId;
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName, cancellationToken);
                
                _logger.LogInformation("User {UserId} unsubscribed from group {GroupName} on connection {ConnectionId}",
                    tenantContext.LoggedInUser, groupName, Context.ConnectionId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("UnsubscribeFromAgent cancelled for connection {ConnectionId}", Context.ConnectionId);
                // Don't send response for cancelled operations
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Service error in UnsubscribeFromAgent on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.ServiceTemporarilyUnavailable, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in UnsubscribeFromAgent on connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync(SignalRMethods.Error, ErrorMessages.FailedToUnsubscribe, cancellationToken);
            }
        }
    }
}
