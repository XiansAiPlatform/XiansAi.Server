using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Features.Shared.Configuration;

/// <summary>
/// Configuration settings for rate limiting policies.
/// </summary>
public class RateLimitSettings
{
    /// <summary>
    /// Whether rate limiting is enabled globally.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Global API rate limit settings.
    /// </summary>
    public RateLimitPolicySettings Global { get; set; } = new();

    /// <summary>
    /// Authentication endpoint rate limit settings (more restrictive).
    /// </summary>
    public RateLimitPolicySettings Authentication { get; set; } = new();

    /// <summary>
    /// Public API rate limit settings (most restrictive).
    /// </summary>
    public RateLimitPolicySettings Public { get; set; } = new();

    /// <summary>
    /// Agent/User API rate limit settings.
    /// </summary>
    public RateLimitPolicySettings AgentUser { get; set; } = new();
}

/// <summary>
/// Settings for individual rate limiting policies.
/// </summary>
public class RateLimitPolicySettings
{
    /// <summary>
    /// Maximum number of requests allowed in the time window.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Time window in seconds.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Number of segments for sliding window (0 = fixed window).
    /// </summary>
    public int SegmentsPerWindow { get; set; } = 0;

    /// <summary>
    /// Queue limit for requests that exceed the rate limit.
    /// </summary>
    public int QueueLimit { get; set; } = 0;
}

/// <summary>
/// Extension methods for configuring rate limiting.
/// </summary>
public static class RateLimitingConfiguration
{
    // Policy names
    public const string GlobalPolicy = "GlobalRateLimit";
    public const string AuthenticationPolicy = "AuthenticationRateLimit";
    public const string PublicApiPolicy = "PublicApiRateLimit";
    public const string AgentUserApiPolicy = "AgentUserApiRateLimit";

    /// <summary>
    /// Adds rate limiting services with configured policies.
    /// </summary>
    public static WebApplicationBuilder AddRateLimiting(this WebApplicationBuilder builder)
    {
        // Load rate limiting settings from configuration
        var rateLimitSettings = builder.Configuration
            .GetSection("RateLimiting")
            .Get<RateLimitSettings>() ?? new RateLimitSettings();

        // Set default values if not configured
        SetDefaultSettings(rateLimitSettings);

        builder.Services.AddRateLimiter(options =>
        {
            // Global rejection handler
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                var endpoint = context.HttpContext.GetEndpoint()?.DisplayName ?? "Unknown";
                var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                
                logger.LogWarning(
                    "Rate limit exceeded for endpoint {Endpoint} from IP {IpAddress}. " +
                    "User: {UserId}, Retry after: {RetryAfter}",
                    endpoint,
                    ipAddress,
                    context.HttpContext.User?.Identity?.Name ?? "Anonymous",
                    context.Lease.GetAllMetadata().FirstOrDefault(m => m.Key == "RETRY_AFTER").Value ?? "Unknown"
                );

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers["Retry-After"] = 
                    context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? ((int)retryAfter.TotalSeconds).ToString()
                        : rateLimitSettings.Global.WindowSeconds.ToString();

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded",
                    message = "Too many requests. Please try again later.",
                    retryAfter = context.HttpContext.Response.Headers["Retry-After"].ToString()
                }, cancellationToken: cancellationToken);
            };

            // Global API policy - Fixed window for general endpoints
            options.AddPolicy(GlobalPolicy, context =>
                CreateRateLimitPartition(
                    context, 
                    rateLimitSettings.Global, 
                    GlobalPolicy
                ));

            // Authentication policy - Sliding window for better protection against brute force
            options.AddPolicy(AuthenticationPolicy, context =>
                CreateRateLimitPartition(
                    context, 
                    rateLimitSettings.Authentication, 
                    AuthenticationPolicy
                ));

            // Public API policy - Most restrictive for unauthenticated endpoints
            options.AddPolicy(PublicApiPolicy, context =>
                CreateRateLimitPartition(
                    context, 
                    rateLimitSettings.Public, 
                    PublicApiPolicy
                ));

            // Agent/User API policy - Balanced for application usage
            options.AddPolicy(AgentUserApiPolicy, context =>
                CreateRateLimitPartition(
                    context, 
                    rateLimitSettings.AgentUser, 
                    AgentUserApiPolicy
                ));
        });

        builder.Services.AddSingleton(rateLimitSettings);

        return builder;
    }

    /// <summary>
    /// Adds rate limiting middleware to the application pipeline.
    /// </summary>
    public static WebApplication UseRateLimitingMiddleware(this WebApplication app)
    {
        var rateLimitSettings = app.Services.GetService<RateLimitSettings>();
        
        if (rateLimitSettings?.Enabled ?? true)
        {
            app.UseRateLimiter();
            app.Logger.LogInformation("Rate limiting middleware enabled");
        }
        else
        {
            app.Logger.LogWarning("Rate limiting is disabled via configuration");
        }

        return app;
    }

    /// <summary>
    /// Creates a rate limit partition based on settings and partitioning key.
    /// </summary>
    private static RateLimitPartition<string> CreateRateLimitPartition(
        HttpContext context,
        RateLimitPolicySettings settings,
        string policyName)
    {
        // Create partition key based on user identity or IP address
        var partitionKey = GetPartitionKey(context, policyName);

        // Use sliding window if segments are configured, otherwise use fixed window
        if (settings.SegmentsPerWindow > 0)
        {
            return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
                new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    SegmentsPerWindow = settings.SegmentsPerWindow,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.QueueLimit
                });
        }
        else
        {
            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.QueueLimit
                });
        }
    }

    /// <summary>
    /// Gets the partition key for rate limiting (user ID, API key, or IP address).
    /// </summary>
    private static string GetPartitionKey(HttpContext context, string policyName)
    {
        // Try to get user identity first (authenticated users)
        var userId = context.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"{policyName}:user:{userId}";
        }

        // Try to get API key from headers
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            // Use hash of API key to avoid storing full key in memory
            var apiKeyHash = apiKey.GetHashCode().ToString();
            return $"{policyName}:apikey:{apiKeyHash}";
        }

        // Fall back to IP address (for anonymous requests)
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // Check for forwarded IP (when behind proxy/load balancer)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            ipAddress = forwardedFor.Split(',')[0].Trim();
        }

        return $"{policyName}:ip:{ipAddress}";
    }

    /// <summary>
    /// Sets default settings for rate limiting if not configured.
    /// </summary>
    private static void SetDefaultSettings(RateLimitSettings settings)
    {
        // Global API defaults - 100 requests per minute
        if (settings.Global.PermitLimit == 0)
        {
            settings.Global.PermitLimit = 100;
            settings.Global.WindowSeconds = 60;
            settings.Global.SegmentsPerWindow = 0; // Fixed window
            settings.Global.QueueLimit = 0;
        }

        // Authentication defaults - 10 requests per minute with sliding window
        if (settings.Authentication.PermitLimit == 0)
        {
            settings.Authentication.PermitLimit = 10;
            settings.Authentication.WindowSeconds = 60;
            settings.Authentication.SegmentsPerWindow = 6; // Sliding window with 6 segments (10 second segments)
            settings.Authentication.QueueLimit = 0;
        }

        // Public API defaults - 30 requests per minute
        if (settings.Public.PermitLimit == 0)
        {
            settings.Public.PermitLimit = 30;
            settings.Public.WindowSeconds = 60;
            settings.Public.SegmentsPerWindow = 0; // Fixed window
            settings.Public.QueueLimit = 0;
        }

        // Agent/User API defaults - 200 requests per minute
        if (settings.AgentUser.PermitLimit == 0)
        {
            settings.AgentUser.PermitLimit = 200;
            settings.AgentUser.WindowSeconds = 60;
            settings.AgentUser.SegmentsPerWindow = 0; // Fixed window
            settings.AgentUser.QueueLimit = 5; // Allow small queue for burst traffic
        }
    }
}

