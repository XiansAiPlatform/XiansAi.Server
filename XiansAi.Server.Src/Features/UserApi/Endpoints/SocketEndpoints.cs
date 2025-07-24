using Features.UserApi.Websocket;

namespace Features.UserApi.Endpoints;

/// <summary>
/// Optimized bot endpoints with minimal overhead and maximum performance
/// </summary>
public static class SocketEndpoints
{
    public static void MapSocketEndpoints(this IEndpointRouteBuilder app)
    {
        // Configure Websocket
        app.MapHub<ChatHub>("/ws/chat");
        app.MapHub<TenantChatHub>("/ws/tenant/chat");
    }
}
