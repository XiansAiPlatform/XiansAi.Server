using System.Threading.RateLimiting;
using Features.Shared.Configuration;
using Shared.Auth;
using Shared.Utils;

namespace Features.AgentApi.Endpoints;

/// <summary>
/// How many notifications each tenant's agents may send (<c>RateLimiting:Notification</c>, 10 per minute
/// by default), so a buggy or compromised agent cannot mass-email its tenant's contacts.
/// </summary>
public sealed class NotificationRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string>? _limiter;

    public NotificationRateLimiter(RateLimitSettings settings)
    {
        if (!settings.Enabled)
            return;

        var policy = settings.Notification;
        _limiter = PartitionedRateLimiter.Create<string, string>(tenantId =>
            RateLimitPartition.GetFixedWindowLimiter(tenantId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                QueueLimit = 0
            }));
    }

    /// <summary>
    /// Takes one notification from the tenant's quota. Returns false, with when to retry, once it is used up.
    /// </summary>
    public bool TryAcquire(string tenantId, out TimeSpan? retryAfter)
    {
        retryAfter = null;
        if (_limiter == null)
            return true;

        using var lease = _limiter.AttemptAcquire(tenantId.Trim().ToLowerInvariant());
        if (lease.IsAcquired)
            return true;

        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var after))
            retryAfter = after;
        return false;
    }

    public void Dispose() => _limiter?.Dispose();
}

/// <summary>
/// Applies <see cref="NotificationRateLimiter"/> to the authenticated tenant. It runs as an endpoint
/// filter, after authentication, so a caller without a valid API key never reaches it and cannot use
/// up another tenant's quota (the ASP.NET rate limiting middleware runs before authentication).
/// </summary>
public sealed class NotificationRateLimitFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var tenantId = httpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId;

        if (!httpContext.RequestServices.GetRequiredService<NotificationRateLimiter>().TryAcquire(tenantId, out var retryAfter))
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("NotificationRateLimit");
            logger.LogWarning("Notification rate limit exceeded for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));

            // Same response as the server's other rate limits.
            var seconds = ((int)Math.Ceiling((retryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds)).ToString();
            httpContext.Response.Headers.RetryAfter = seconds;
            return Results.Json(new
            {
                error = "Rate limit exceeded",
                message = "Too many requests. Please try again later.",
                retryAfter = seconds
            }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        return await next(context);
    }
}
