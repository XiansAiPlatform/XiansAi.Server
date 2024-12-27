using System.IdentityModel.Tokens.Jwt;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;
    private const string TENANT_CLAIM_TYPE = "https://flowmaxer.ai/tenant";
    private const string AUTHORIZATION_HEADER = "Authorization";

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
    {
        var token = ExtractToken(context);

        if (!token.Success)
        {
            await HandleError(context, token.Message, StatusCodes.Status401Unauthorized);
            return;
        }

        var (isValid, tenantId) = ValidateAndExtractTenantId(token.Value!);
        if (!isValid)
        {
            await HandleError(context, tenantId, StatusCodes.Status403Forbidden);
            return;
        }

        await SetupTenantContextAndContinue(context, serviceProvider, tenantId);
    }

    private (bool Success, string? Value, string Message) ExtractToken(HttpContext context)
    {
        var token = context.Request.Headers[AUTHORIZATION_HEADER]
            .FirstOrDefault()?.Split(" ").Last();

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Authorization token is missing.");
            return (false, null, "Authorization token is missing.");
        }

        return (true, token, "Token extracted");
    }

    private (bool IsValid, string TenantId) ValidateAndExtractTenantId(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadToken(token);
        var tokenS = jsonToken as JwtSecurityToken;

        if (tokenS == null)
        {
            _logger.LogWarning("Invalid JWT token format.");
            return (false, "Invalid token format.");
        }

        var tenantId = tokenS.Claims.FirstOrDefault(c => c.Type == TENANT_CLAIM_TYPE)?.Value;

        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Tenant ID is missing in app_metadata.");
            return (false, $"Tenant ID is missing in tenant custom claim {TENANT_CLAIM_TYPE}");
        }

        return (true, tenantId);
    }

    private async Task SetupTenantContextAndContinue(HttpContext context, IServiceProvider serviceProvider, string tenantId)
    {
        _logger.LogInformation("Creating tenant scope for tenant {tenantId}", tenantId);
        var tenantContext = serviceProvider.GetRequiredService<TenantContext>();
        tenantContext.TenantId = tenantId;

        await _next(context);
    }

    private async Task HandleError(HttpContext context, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(message);
    }
}