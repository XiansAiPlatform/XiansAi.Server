using Features.PublicApi.Endpoints;
using Features.PublicApi.Services;
using System.Threading.RateLimiting;

namespace Features.PublicApi.Configuration;

public static class PublicApiConfiguration
{
    public static WebApplicationBuilder AddPublicApiServices(this WebApplicationBuilder builder)
    {
        // Register Public API specific services (no authentication services needed)
        builder.Services.AddScoped<IPublicRegistrationService, PublicRegistrationService>();
        
        // Add rate limiting services
        builder.Services.AddRateLimiter(rateLimiterOptions =>
        {
            // Global rate limit for all public endpoints - 100 requests per minute per IP
            rateLimiterOptions.AddPolicy("PublicApiGlobal", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 10
                    }));

            // Stricter rate limit for POST endpoints - 20 requests per minute per IP
            rateLimiterOptions.AddPolicy("PublicApiPost", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5
                    }));

            // Very strict rate limit for registration endpoints - 5 requests per minute per IP
            rateLimiterOptions.AddPolicy("PublicApiRegistration", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    }));

            // Very permissive rate limit for GET endpoints - 200 requests per minute per IP
            rateLimiterOptions.AddPolicy("PublicApiGet", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 200,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 20
                    }));

            // Global fallback - reject requests that exceed all limits
            rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 500, // Very high global limit - 500 requests per minute per IP
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // Customize the response when rate limit is exceeded
            rateLimiterOptions.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                context.HttpContext.Response.ContentType = "application/json";
                
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue) 
                    ? retryAfterValue.TotalSeconds.ToString() 
                    : "60";
                
                context.HttpContext.Response.Headers["Retry-After"] = retryAfter;
                
                var response = new
                {
                    error = "Rate limit exceeded",
                    message = "Too many requests. Please try again later.",
                    retryAfterSeconds = retryAfter,
                    timestamp = DateTime.UtcNow
                };
                
                await context.HttpContext.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(response), token);
            };
        });
        
        return builder;
    }
    
    public static WebApplication UsePublicApiEndpoints(this WebApplication app)
    {
        // Enable rate limiting middleware for Public API
        app.UseRateLimiter();
        
        // Map Public API endpoints (no authentication required, but rate limited)
        RegisterEndpoints.MapRegisterEndpoints(app);
        GitHubAuthEndpoints.MapGitHubAuthEndpoints(app);
        
        return app;
    }
}
