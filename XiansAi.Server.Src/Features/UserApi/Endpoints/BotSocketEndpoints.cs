using Features.UserApi.Websocket;

namespace Features.UserApi.Endpoints;

/// <summary>
/// Optimized bot endpoints with minimal overhead and maximum performance
/// </summary>
public static class BotSocketEndpoints
{
    public static void MapBotSocketEndpoints(this IEndpointRouteBuilder app)
    {
        // Configure Websocket
        app.MapHub<BotHub>("/ws/user/bot");
    }
}
