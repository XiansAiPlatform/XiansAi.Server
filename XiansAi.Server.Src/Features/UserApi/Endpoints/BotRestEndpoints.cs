using Microsoft.AspNetCore.Mvc;
using Features.UserApi.Services;
using Features.UserApi.Websocket;
using Shared.Utils.Services;

namespace Features.UserApi.Endpoints;

/// <summary>
/// Optimized bot endpoints with minimal overhead and maximum performance
/// </summary>
public static class BotRestEndpoints
{
    public static void MapBotRestEndpoints(this IEndpointRouteBuilder app)
    {
        // Configure API
        var botGroup = app.MapGroup("/api/user/bot")
            .RequireAuthorization("EndpointAuthPolicy")
            .WithTags("Optimized Bot API");

        // High-performance bot request processing
        botGroup.MapPost("/", async (
            [FromBody] BotRequest request, 
            [FromServices] IBotService botService) =>
        {
            var result = await botService.ProcessBotRequestAsync(request);
            return result.ToHttpResult();
        })
        .WithName("ProcessOptimizedBotChat")
        .WithSummary("Process bot chat request with optimized performance")
        .WithDescription("Handles bot chat requests with minimal latency and maximum throughput");

        // Optimized bot history retrieval with caching
        botGroup.MapGet("/history/{workflowId}/{participantId}", async (
            [FromRoute] string workflowId,
            [FromRoute] string participantId,
            [FromServices] IBotService botService,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? scope = null) =>
        {
            var result = await botService.GetBotHistoryAsync(workflowId, participantId, page, pageSize, scope);
            return result.ToHttpResult();
        })
        .WithName("GetOptimizedBotHistory")
        .WithSummary("Get bot conversation history with caching")
        .WithDescription("Retrieves bot conversation history with optimized database queries and caching");

        botGroup.MapGet("/metrics", () =>
        {
            return Results.Ok(new
            {
                Timestamp = DateTime.UtcNow,
                Service = "BotService",
                HubMetrics = BotHub.GetConnectionMetrics(),
                Status = "Healthy"
            });
        })
        .WithName("GetBotMetrics")
        .WithSummary("Get bot service performance metrics")
        .WithDescription("Returns performance metrics and connection statistics");
    }
}