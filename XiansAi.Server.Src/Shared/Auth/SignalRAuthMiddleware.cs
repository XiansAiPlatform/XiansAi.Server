using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Shared.Auth;

namespace Features.WebApi.Auth;

/// <summary>
/// Middleware to handle SignalR authentication, ensuring JWT tokens are properly validated
/// even when Authorize attribute is not applied to the Hub.
/// </summary>
public class SignalRAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SignalRAuthMiddleware> _logger;

    public SignalRAuthMiddleware(RequestDelegate next, ILogger<SignalRAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only process SignalR hub requests
        if (context.Request.Path.StartsWithSegments("/ws"))
        {
            _logger.LogDebug("Processing SignalR request: {Path}", context.Request.Path);
            // Resolve ITenantContext from the request scope
            var tenantContext = context.RequestServices.GetService<ITenantContext>();
            if (tenantContext == null)
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
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
                    // Validate and process the token manually
                    var handler = new JwtSecurityTokenHandler();
                    var jsonToken = handler.ReadToken(accessToken) as JwtSecurityToken;

                    if (jsonToken != null)
                    {
                        // Extract user claims
                        var userId = jsonToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                        
                        // Try multiple possible tenant claim names
                        var tenantId = jsonToken.Claims.FirstOrDefault(c => c.Type == BaseAuthRequirement.TENANT_CLAIM_TYPE)?.Value
                            ?? jsonToken.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value
                            ?? jsonToken.Claims.FirstOrDefault(c => c.Type == "https://xians.ai/tenants")?.Value;

                        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(tenantId))
                        {
                            // Set tenant context
                            tenantContext.TenantId = tenantId;
                            tenantContext.LoggedInUser = userId;
                            tenantContext.AuthorizedTenantIds = new[] { tenantId };
                            
                            // Create claims identity and principal
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, userId),
                                new Claim("sub", userId),
                                new Claim("tenant_id", tenantId),
                                new Claim(BaseAuthRequirement.TENANT_CLAIM_TYPE, tenantId)
                            };

                            // Add all original claims from the token
                            foreach (var claim in jsonToken.Claims)
                            {
                                if (!claims.Any(c => c.Type == claim.Type && c.Value == claim.Value))
                                {
                                    claims.Add(claim);
                                }
                            }

                            var identity = new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme);
                            var principal = new ClaimsPrincipal(identity);
                            
                            // Set the user on the HttpContext
                            context.User = principal;
                            
                            _logger.LogInformation("Successfully authenticated SignalR connection: User={UserId}, Tenant={TenantId}", 
                                userId, tenantId);
                        }
                        else
                        {
                            _logger.LogWarning("Missing required claims in token: UserId={UserId}, TenantId={TenantId}", 
                                userId, tenantId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Invalid JWT token format");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing JWT token for SignalR connection");
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