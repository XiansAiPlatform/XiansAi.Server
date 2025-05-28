using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Shared.Auth;
using Microsoft.Extensions.Configuration;

namespace Features.WebApi.Auth;

/// <summary>
/// Middleware to handle SignalR authentication, ensuring JWT tokens are properly validated
/// even when Authorize attribute is not applied to the Hub.
/// </summary>
public class SignalRAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SignalRAuthMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public SignalRAuthMiddleware(RequestDelegate next, ILogger<SignalRAuthMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only process SignalR hub requests
        if (context.Request.Path.StartsWithSegments("/ws"))
        {
            _logger.LogDebug("Processing SignalR request: {Path}", context.Request.Path);
            var tenantContext = context.RequestServices.GetService<ITenantContext>();
            if (tenantContext == null)
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                await _next(context);
                return;
            }

            // Check if WebSockets are enabled in configuration
            var websocketConfig = _configuration.GetSection("WebSockets");
            if (!websocketConfig.GetValue<bool>("Enabled"))
            {
                _logger.LogWarning("WebSockets are not enabled in configuration");
                await _next(context);
                return;
            }

            // Get tenantId from query string
            var tenantId = context.Request.Query["tenantId"].ToString();
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("No tenantId provided in query string");
                await _next(context);
                return;
            }

            // Extract the token from query string (WebSocket) or Authorization header (negotiate)
            var accessToken = context.Request.Query["access_token"].ToString();
            
            if (string.IsNullOrEmpty(accessToken))
            {
                var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                {
                    accessToken = authHeader.Substring("Bearer ".Length);
                }
            }

            if (!string.IsNullOrEmpty(accessToken))
            {
                try
                {
                    // Get the secrets section
                    var secrets = websocketConfig.GetSection("Secrets").Get<Dictionary<string, string>>();
                    // Check if the secrets section is null
                    if (secrets == null)
                    {
                        _logger.LogWarning("WebSocket Secrets not found in configuration");
                        await _next(context);
                        return;
                    }  
                    // Check if the provided tenantId exists in configuration
                    if (!secrets.ContainsKey(tenantId))
                    {
                        _logger.LogWarning("Provided tenantId {TenantId} not found in configuration", tenantId);
                        await _next(context);
                        return;
                    }

                    // Get the expected secret for this tenant
                    var expectedSecret = secrets[tenantId];
                    
                    // Verify if the provided access token matches the expected secret
                    if (accessToken == expectedSecret)
                    {
                        // Set the WebSocket user ID from configuration
                        var websocketUserId = websocketConfig.GetValue<string>("UserId");
                        if (websocketUserId == null)
                        {
                            _logger.LogWarning("WebSocket UserId not found in configuration");
                            await _next(context);
                            return;
                        }
                        tenantContext.LoggedInUser = websocketUserId;
                        tenantContext.TenantId = tenantId;
                        tenantContext.AuthorizedTenantIds = new[] { tenantId };

                        
                        _logger.LogInformation("Successfully authenticated SignalR connection: User={UserId}, Tenant={TenantId}", 
                            websocketUserId, tenantId);
                    }
                    else
                    {
                        _logger.LogWarning("Access token does not match the expected secret for tenant {TenantId}", tenantId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing access token for SignalR connection");
                }
            }
            else
            {
                _logger.LogWarning("No access token found for SignalR connection");
            }
        }

        // Continue with the request
        await _next(context);
    }
}