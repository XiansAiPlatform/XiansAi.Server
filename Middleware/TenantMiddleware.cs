public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;
    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        // Determine the tenant from the request (e.g., from headers, subdomain, etc.)
        // TODO: This should be taken from a token in the future
        var tenantId = context.Request.Headers["X-Tenant-ID"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Tenant ID is missing. Falling back to 'Default' tenant.");
            tenantContext.TenantId = "Default";
        }
        else
        {
            // Set the tenant context
            tenantContext.TenantId = tenantId;
        }

        // Ensure the request continues through the pipeline
        await _next(context);
    }
}