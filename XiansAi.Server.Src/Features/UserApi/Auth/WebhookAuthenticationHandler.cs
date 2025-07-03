using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Features.UserApi.Auth
{
    public class WebhookAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<WebhookAuthenticationHandler> _logger;
        private readonly IApiKeyService _apiKeyService;


        public WebhookAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ITenantContext tenantContext,
            IApiKeyService apiKeyService)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<WebhookAuthenticationHandler>();
            _tenantContext = tenantContext;

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
                        if (accessToken.StartsWith("sk-Xnai-"))
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

                                var identity = new ClaimsIdentity(claims, Scheme.Name);
                                var principal = new ClaimsPrincipal(identity);
                                var ticket = new AuthenticationTicket(principal, Scheme.Name);
                                _logger.LogInformation("Successfully authenticated Webhook connection: User={UserId}, Tenant={TenantId}", apiKey.CreatedBy, tenantId);

                                return AuthenticateResult.Success(ticket);
                            }
                            else
                            {
                                _logger.LogWarning("Invalid TenantID {TenantId}", tenantId);
                                return AuthenticateResult.Fail("Access denied Invalid TenantID");
                            }
                        }
                        else if (accessToken.Count(c => c == '.') == 2)
                        {
                            // Treat as JWT
                            // TODO: Add your JWT validation logic here
                            _logger.LogWarning("JWT authentication not implemented");
                            return AuthenticateResult.Fail("JWT authentication not implemented");
                        }
                        else
                        {
                            _logger.LogWarning("Invalid token format");
                            return AuthenticateResult.Fail("Invalid token format");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing access token for Webhook connection");
                        return AuthenticateResult.Fail("Error processing access token for Webhook connection");
                    }
                }
                else
                {
                    _logger.LogWarning("No ApiKey found for Webhook trigger");
                    return AuthenticateResult.Fail("No ApiKey found for Webhook trigger");
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

