using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;

namespace XiansAi.Server.Shared.Websocket
{
    [Authorize(Policy = "WebsocketAuthPolicy")]
    public class ChatHub : Hub
    {
        private readonly IMessageService _messageService;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantContext? _tempTenantContext;
        private readonly ILogger<ChatHub> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationCacheService _authorizationCacheService;

        public ChatHub(IMessageService messageService, ITenantContext tenantContext, IHttpContextAccessor httpContextAccessor, ILogger<ChatHub> logger, IAuthorizationCacheService authorizationCacheService)
        {
            _messageService = messageService;
            _httpContextAccessor = httpContextAccessor;
            _authorizationCacheService = authorizationCacheService;
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
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_tenantContext.TenantId) || string.IsNullOrEmpty(_tenantContext.LoggedInUser))
                {
                    _logger.LogError("TenantContext not properly initialized");
                    await Clients.Caller.SendAsync("ConnectionError", new
                    {
                        StatusCode = StatusCodes.Status401Unauthorized,
                        Message = "Invalid tenant context"
                    });
                    Context.Abort();
                    return;
                }

                _logger.LogInformation("SignalR Connection established for user: {UserId}, Tenant: {TenantId}",
                    _tenantContext.LoggedInUser, _tenantContext.TenantId);

                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnectedAsync");
                await Clients.Caller.SendAsync("ConnectionError", new
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                    Message = "Internal server error"
                });
                Context.Abort();
            }
        }
        private async Task SetAuthorizationFromHeader(ChatOrDataRequest request)
        {
            if (request.Authorization == null)
            {
                var userAccessGuid = Context.User?.Claims?.FirstOrDefault(c => c.Type == "UserAccessGuid")?.Value;
                if (!string.IsNullOrEmpty(userAccessGuid))
                {
                    var token = await _authorizationCacheService.GetAuthorization(userAccessGuid);
                    _logger.LogDebug("UserAccessGuid found in context: {UserAccessGuid}", userAccessGuid);
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Authorization = token;
                        _logger.LogDebug("Authorization token set from UserAccessGuid: {Token}", token);
                    }
                    else
                    {
                        _logger.LogDebug("Token not found");
                        Context.Abort();
                    }
                }
                else
                {
                    _logger.LogWarning("UserAccessGuid claim not found in context");
                }
            }
        }
        private void EnsureTenantContext()
        {
            if (_tempTenantContext == null) throw new InvalidOperationException("TenantContext not properly initialized");

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

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            return base.OnDisconnectedAsync(exception);
        }

        public async Task GetThreadHistory(string workflowType, string participantId, int page, int pageSize)
        {
            EnsureTenantContext();
            var result = await _messageService.GetThreadHistoryAsync(workflowType, participantId, page, pageSize);
            await Clients.Caller.SendAsync("ThreadHistory", result.Data);
        }

        //TODO: This is a temporary method to send inbound messages to the client. We need to refactor this to use the new messaging endpoints.
        public async Task SendInboundMessage(ChatOrDataRequest request, string messageType)
        {
            _logger.LogDebug("Sending inbound message : {Request}", JsonSerializer.Serialize(request));
            EnsureTenantContext();
            // Extract UserAccessGuid from claims

            await SetAuthorizationFromHeader(request);
            //SetAuthorizationFromHeader(request, _httpContextAccessor.HttpContext!);
            try
            {
                var messageTypeEnum = Enum.Parse<MessageType>(messageType);
                // Step 1: Process inbound
                //var inboundResult = await _messageService.ProcessIncomingMessage(request, messageTypeEnum);

                //if (inboundResult.Data != null)
                //{
                //    // Notify client message was received
                //    await Clients.Caller.SendAsync("InboundProcessed", inboundResult.Data);
                //}
                //else
                //{
                //    await Clients.Caller.SendAsync("InboundProcessed", inboundResult.StatusCode);
                //    Context.Abort();
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing inbound message");
                await Clients.Caller.SendAsync("Error", "Failed to process message");
            }
        }

        public async Task SubscribeToAgent(string workflowId, string participantId, string TenantId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, workflowId + participantId + TenantId);
        }

        public async Task UnsubscribeFromAgent(string workflowId, string participantId, string TenantId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, workflowId + participantId + TenantId);
        }
    }
}
