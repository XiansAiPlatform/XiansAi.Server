using Microsoft.AspNetCore.Mvc;

namespace Features.PublicApi.Endpoints;

public static class SampleEndpoints
{
    public static void MapSampleEndpoints(this WebApplication app)
    {
        // Map sample endpoints without authentication
        var sampleGroup = app.MapGroup("/api/public/sample")
            .WithTags("PublicAPI - Sample");

        sampleGroup.MapGet("/hello", () =>
        {
            return Results.Ok(new { 
                message = "Hello from Public API!", 
                timestamp = DateTime.UtcNow,
                status = "success"
            });
        })
        .WithName("Get Hello Message")
        .WithOpenApi(operation => {
            operation.Summary = "Get a hello message";
            operation.Description = "Returns a simple hello message with timestamp. No authentication required. Rate limited to 200 requests per minute per IP.";
            return operation;
        })
        .RequireRateLimiting("PublicApiGet");

        sampleGroup.MapGet("/info", () =>
        {
            return Results.Ok(new { 
                service = "XiansAi Public API",
                version = "1.0.0",
                description = "Public endpoints that don't require authentication but are rate limited",
                timestamp = DateTime.UtcNow,
                rateLimits = new {
                    getEndpoints = "200 requests per minute per IP",
                    postEndpoints = "20 requests per minute per IP",
                    globalLimit = "500 requests per minute per IP"
                },
                endpoints = new[] {
                    "/api/public/sample/hello",
                    "/api/public/sample/info",
                    "/api/public/sample/echo"
                }
            });
        })
        .WithName("Get API Info")
        .WithOpenApi(operation => {
            operation.Summary = "Get API information";
            operation.Description = "Returns information about the public API endpoints and rate limits. No authentication required. Rate limited to 200 requests per minute per IP.";
            return operation;
        })
        .RequireRateLimiting("PublicApiGet");

        sampleGroup.MapPost("/echo", (
            [FromBody] object data) =>
        {
            return Results.Ok(new { 
                message = "Echo response",
                receivedData = data,
                timestamp = DateTime.UtcNow,
                status = "success"
            });
        })
        .WithName("Echo Data")
        .WithOpenApi(operation => {
            operation.Summary = "Echo received data";
            operation.Description = "Returns the data that was sent in the request body. No authentication required. Rate limited to 20 requests per minute per IP.";
            return operation;
        })
        .RequireRateLimiting("PublicApiPost");
    }
}
