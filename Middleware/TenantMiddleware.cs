

using System.IdentityModel.Tokens.Jwt;

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
        // Extract the token from the Authorization header
        var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Authorization token is missing.");
            context.Response.StatusCode = 401; // Unauthorized
            await context.Response.WriteAsync("Authorization token is missing.");
            return;
        }

        // Decode and validate the JWT token
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadToken(token);
        var tokenS = jsonToken as JwtSecurityToken;

        if (tokenS == null)
        {
            _logger.LogWarning("Invalid JWT token format.");
            await context.Response.WriteAsync("Invalid token format.");
            context.Response.StatusCode = 401;
            return;
        }
        const string tenantClaimType = "https://flowmaxer.ai/tenant";
        var tenantId = tokenS.Claims.FirstOrDefault(c => c.Type == tenantClaimType)?.Value;

        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Tenant ID is missing in app_metadata. ");
            context.Response.StatusCode = 403; // Unauthorized        
            await context.Response.WriteAsync($"Tenant ID is missing in tenant custom claim {tenantClaimType}");
            return;
        }

        // Set the tenant context
        tenantContext.TenantId = tenantId;

        // Ensure the request continues through the pipeline
        await _next(context);
    }
}