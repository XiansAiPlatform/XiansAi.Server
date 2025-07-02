using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;
using XiansAi.Server.Shared.Services;

namespace XiansAi.Server.Shared.Auth
{
    public class WebhookAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<WebhookAuthenticationHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IAuthorizationCacheService _authorizationCacheService;
        private readonly IApiKeyService _apiKeyService;


        public WebhookAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            IAuthorizationCacheService authorizationCacheService,
            UrlEncoder encoder,
            IConfiguration configuration,
            ITenantContext tenantContext,
            IApiKeyService apiKeyService)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<WebhookAuthenticationHandler>();
            _tenantContext = tenantContext;
            _configuration = configuration;
            _authorizationCacheService = authorizationCacheService;
            _apiKeyService = apiKeyService;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Example: validate access_token and tenant from query
            var accessToken = string.Empty;
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                accessToken = authHeader.Substring("Bearer ".Length);
                Console.WriteLine($"Access Token from Header: {accessToken}");
            }
            else
            {
                return AuthenticateResult.Fail("Invalid API key or Tenant ID");
            }

            var tenantId = Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? Request.Query["tenantId"].ToString();
            var userAccessToken = Request.Headers["X-Secondary-Token"].FirstOrDefault() ?? null;
            string? accessGuid = null;
            if (!string.IsNullOrEmpty(userAccessToken))
            {
                accessGuid = await _authorizationCacheService.CacheAuthorization(userAccessToken);
            }
          
            if (_tenantContext != null)
            {

                if (string.IsNullOrEmpty(tenantId))
                {
                    return AuthenticateResult.Fail("No tenantId provided in query string");
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    //var authHeader = Request.Headers["Authorization"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                    {
                        accessToken = authHeader.Substring("Bearer ".Length);
                    }
                }

                if (!string.IsNullOrEmpty(accessToken))
                {
                    try
                    {
                        // Get the apikey object
                        var apiKey = await _apiKeyService.GetApiKeyByRawKeyAsync(accessToken, tenantId);
                        // Check if the apikey section is null
                        if (apiKey == null)
                        {
                            _logger.LogWarning("Webhook apikey not found");
                            return AuthenticateResult.Fail("Invalid API key or Tenant ID");
                        }
                      
                        // Verify if the provided tenant matches the expected tenant
                        if (tenantId == apiKey.TenantId)
                        {

                            _tenantContext.LoggedInUser = apiKey.CreatedBy;
                            _tenantContext.TenantId = apiKey.TenantId;
                            _tenantContext.AuthorizedTenantIds = new[] { apiKey.TenantId };

                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, apiKey.CreatedBy),
                                new Claim("TenantId", apiKey.TenantId)
                            };
                            //if (!string.IsNullOrEmpty(accessGuid))
                            //{
                            //    claims.Add(new Claim("UserAccessGuid", accessGuid));
                            //}

                            var identity = new ClaimsIdentity(claims, Scheme.Name);
                            var principal = new ClaimsPrincipal(identity);
                            var ticket = new AuthenticationTicket(principal, Scheme.Name);
                            _logger.LogInformation("Successfully authenticated Webhook connection: User={UserId}, Tenant={TenantId}", apiKey.CreatedBy, tenantId);

                            return AuthenticateResult.Success(ticket);
                        }
                        else
                        {
                            _logger.LogWarning("Access token does not match the expected secret for tenant {TenantId}", tenantId);
                            return AuthenticateResult.Fail("Access token does not match the expected secret for tenant");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing access token for SignalR connection");
                        return AuthenticateResult.Fail("Error processing access token for SignalR connection");
                    }
                }
                else
                {
                    _logger.LogWarning("No access token found for SignalR connection");
                    return AuthenticateResult.Fail("No access token found for SignalR connection");
                }
            }
            else
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                return AuthenticateResult.Fail("Failed to resolve ITenantContext from request scope");
            }
        }
    }
}

