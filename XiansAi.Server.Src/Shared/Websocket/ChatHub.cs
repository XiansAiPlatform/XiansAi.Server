using Microsoft.AspNetCore.SignalR;
using Shared.Auth;
using Shared.Services;

namespace XiansAi.Server.Shared.Websocket
{   
    public class ChatHub : Hub
    {
        private readonly ClientConnectionManager _connectionManager;
        private readonly IMessageService _messageService;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantContext? _tempTenantContext;
        private readonly ILogger<ChatHub> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ChatHub(ClientConnectionManager connectionManager, IMessageService messageService, ITenantContext tenantContext, IHttpContextAccessor httpContextAccessor, ILogger<ChatHub> logger)
        {
            _connectionManager = connectionManager;
            _messageService = messageService;
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
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_tenantContext.TenantId) || string.IsNullOrEmpty(_tenantContext.LoggedInUser))
                {
                    _logger.LogError("TenantContext not properly initialized");
                    await Clients.Caller.SendAsync("ConnectionError", new { 
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
                await Clients.Caller.SendAsync("ConnectionError", new { 
                    StatusCode = StatusCodes.Status500InternalServerError, 
                    Message = "Internal server error" 
                });
                Context.Abort();
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

        public void RegisterThread(string threadId)
        {
            EnsureTenantContext();
            _connectionManager.AddConnection(threadId, Context.ConnectionId);
            Console.WriteLine($"Registered thread {threadId} with connection {Context.ConnectionId}");
        }

        public void DisconnectThread(string threadId)
        {
            if (_connectionManager.GetConnectionId(threadId)==Context.ConnectionId)
            {
                _connectionManager.RemoveConnection(Context.ConnectionId);
                Console.WriteLine($"Disconnected thread {threadId} and removed connection {Context.ConnectionId}");
            }

            Context.Abort(); // This forcibly disconnects the client from the hub           
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _connectionManager.RemoveConnection(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        public async Task GetThreadHistory(string agent, string workflowType, string participantId, int page, int pageSize)
        {
            EnsureTenantContext();
            var result = await _messageService.GetThreadHistoryAsync(agent, workflowType, participantId, page, pageSize);
            await Clients.Caller.SendAsync("ThreadHistory", result.Data);           
        }

        public async Task SendInboundMessage(MessageRequest request)
        {
            EnsureTenantContext();
            try 
            {
                // Step 1: Process inbound
                var inboundResult = await _messageService.ProcessIncomingMessage(request);
                
                if (inboundResult.Data != null)
                {
                    _connectionManager.AddConnection(inboundResult.Data, Context.ConnectionId);
                                      
                    // Notify client message was received
                    await Clients.Caller.SendAsync("InboundProcessed", inboundResult.Data);
                }
                else
                {
                    await Clients.Caller.SendAsync("InboundProcessed", inboundResult.StatusCode);
                    Context.Abort();
                }
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
