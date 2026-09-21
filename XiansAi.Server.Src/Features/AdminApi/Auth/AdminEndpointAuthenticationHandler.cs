using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Exceptions;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Shared.Data.Models;
using Shared.Utils;

namespace Features.AdminApi.Auth
{
    public class AdminEndpointAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>
        /// HttpContext.Items key used to surface the specific authentication failure reason
        /// to the authorization result handler, so the 401 response carries a meaningful
        /// message instead of a generic one.
        /// </summary>
        public const string FailureReasonItemKey = "AdminApi.AuthFailureReason";
        public const string UserTokenHeaderName = "X-User-Token";

        private readonly ITenantContext _tenantContext;
        private readonly ILogger<AdminEndpointAuthenticationHandler> _logger;
        private readonly IApiKeyService _apiKeyService;
        private readonly IAdminRoleTenantResolver _adminRoleTenantResolver;
        private readonly IAdminKeylessUserResolver _keylessUserResolver;

        private AuthenticateResult FailWithReason(string reason)
        {
            Context.Items[FailureReasonItemKey] = reason;
            return AuthenticateResult.Fail(reason);
        }

        public AdminEndpointAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ITenantContext tenantContext,
            IApiKeyService apiKeyService,
            IAdminRoleTenantResolver adminRoleTenantResolver,
            IAdminKeylessUserResolver keylessUserResolver)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<AdminEndpointAuthenticationHandler>();
            _tenantContext = tenantContext;
            _apiKeyService = apiKeyService;
            _adminRoleTenantResolver = adminRoleTenantResolver;
            _keylessUserResolver = keylessUserResolver;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Only handle authentication for AdminApi endpoints
            // Check if path matches /api/{version}/admin pattern (supports v1, v2, etc.)
            var path = Request.Path.Value?.ToLowerInvariant() ?? "";
            var adminApiPattern = "/api/";
            var adminSuffix = "/admin";
            
            _logger.LogDebug("AdminEndpointAuthenticationHandler: Evaluating path '{Path}'", LogSanitizer.Sanitize(Request.Path));
            
            // Check if path matches AdminApi pattern: /api/{version}/admin
            if (!path.StartsWith(adminApiPattern) || !path.Contains(adminSuffix))
            {
                _logger.LogDebug("Skipping admin endpoint authentication for non-AdminApi path: {Path}", LogSanitizer.Sanitize(Request.Path));
                return AuthenticateResult.NoResult(); // Let other handlers process this request
            }
            
            // Verify it's actually an AdminApi path (not just any /api/... path)
            var pathParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Length < 3 || pathParts[0] != "api" || pathParts[2] != "admin")
            {
                _logger.LogDebug("Skipping admin endpoint authentication for non-AdminApi path: {Path}", LogSanitizer.Sanitize(Request.Path));
                return AuthenticateResult.NoResult(); // Let other handlers process this request
            }

            _logger.LogDebug("Processing AdminApi endpoint request: {Path}", LogSanitizer.Sanitize(Request.Path));

            // Extract API key from Authorization header only.
            // Query parameter (?apikey=) is not supported: it can leak into reverse-proxy logs, CDN logs, and browser history.
            var accessToken = string.Empty;
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                accessToken = authHeader.Substring("Bearer ".Length).Trim();
            }
            
            // Extract tenantId from multiple sources in priority order:
            // 1. Query parameter (tenantId=)
            // 2. Route parameter (e.g., /tenants/{tenantId})
            // 3. X-Tenant-Id header
            var tenantId = Request.Query["tenantId"].ToString();
            if (string.IsNullOrEmpty(tenantId))
            {
                // Try route parameter
                if (Request.RouteValues.TryGetValue("tenantId", out var routeTenantId) && routeTenantId != null)
                {
                    tenantId = routeTenantId.ToString() ?? string.Empty;
                }
            }
            if (string.IsNullOrEmpty(tenantId))
            {
                // Try X-Tenant-Id header
                tenantId = Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? string.Empty;
            }
            
            // Preserve the original tenantId from request for validation
            var originalTenantIdFromRequest = tenantId;
            
            // Note: tenantId is now optional. If not provided, it will be derived from the API key.
            // This prevents IDOR vulnerabilities by ensuring the tenant matches the authenticated credential.

            _logger.LogDebug("Processing AdminApi Endpoint request: {Path}", LogSanitizer.Sanitize(Request.Path));
            if (_tenantContext != null)
            {

                if (!string.IsNullOrEmpty(accessToken))
                {
                    try
                    {
                        // Only API keys starting with "sk-Xnai-" are supported
                        if (!accessToken.StartsWith("sk-Xnai-"))
                        {
                            _logger.LogWarning("Invalid API key format. API key must start with 'sk-Xnai-'");
                            return FailWithReason("Invalid API key format");
                        }

                        // Look up API key
                        ApiKey? apiKey;
                        
                        // Always look up API key by token first to establish identity
                        // This allows SysAdmins (Tenant=System) to access other tenants (Tenant=Target)
                        apiKey = await _apiKeyService.GetApiKeyByRawKeyAsync(accessToken);

                        if (apiKey == null)
                        {
                            _logger.LogWarning("Invalid API key submitted");
                            return FailWithReason("Invalid API key");
                        }

                        var resolutionResult = await _adminRoleTenantResolver.ResolveAsync(
                            apiKey.CreatedBy, apiKey, originalTenantIdFromRequest);

                        if (!resolutionResult.Success)
                        {
                            return FailWithReason(resolutionResult.ErrorMessage ?? "Authorization failed");
                        }

                        var finalTenantId = resolutionResult.FinalTenantId!;
                        var userRoles = resolutionResult.UserRoles!;
                        // Prefer the canonical user_id when the key owner was stored as an email.
                        var resolvedUserId = resolutionResult.ResolvedUserId ?? apiKey.CreatedBy;

                        _logger.LogDebug("Setting tenant context with user ID: {userId}, user type: {userType}, and roles: {roles}",
                            LogSanitizer.RedactUserId(resolvedUserId), UserType.UserApiKey, string.Join(", ", userRoles));
                        _tenantContext.LoggedInUser = resolvedUserId;
                        _tenantContext.UserType = UserType.UserApiKey;
                        _tenantContext.TenantId = finalTenantId;
                        _tenantContext.UserRoles = userRoles.ToArray();
                        _tenantContext.AuthorizedTenantIds = new[] { finalTenantId };
                        _tenantContext.Authorization = accessToken;
                        AdminOnBehalfOfBinder.Apply(Request, _tenantContext, _logger);

                        _logger.LogInformation("Successfully authenticated AdminApi connection: User={UserId}, Tenant={TenantId}, Roles={Roles}",
                            LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(finalTenantId), LogSanitizer.Sanitize(string.Join(", ", userRoles)));

                        return AuthenticateResult.Success(BuildTicket(resolvedUserId, finalTenantId));
                    }
                    catch (TenantNotFoundException)
                    {
                        // Re-throw so global exception handler can return 404 (not 401)
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing access token for AdminApi Endpoint connection");
                        return FailWithReason("Error processing access token for AdminApi Endpoint connection");
                    }
                }
                else
                {
                    return await HandleUserTokenAuthenticationAsync(originalTenantIdFromRequest);
                }
            }
            else
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                return FailWithReason("Failed to resolve ITenantContext from request scope");
            }
        }

        /// <summary>
        /// Authenticates ID-token-only AdminApi caller.
        /// Resolves identity and roles via <see cref="_keylessUserResolver"/>.
        /// </summary>
        private async Task<AuthenticateResult> HandleUserTokenAuthenticationAsync(string originalTenantIdFromRequest)
        {
            var userToken = Request.Headers[UserTokenHeaderName].FirstOrDefault();
            if (string.IsNullOrEmpty(userToken))
            {
                _logger.LogWarning("No access token found for AdminApi Endpoint connection");
                return FailWithReason("No access token found for AdminApi Endpoint connection");
            }

            try
            {
                var tenantOptionalForSysAdmin =
                    Context.GetEndpoint()?.Metadata.GetMetadata<TenantOptionalForSysAdminMetadata>() != null;
                var resolution = await _keylessUserResolver.ResolveAsync(
                    userToken, originalTenantIdFromRequest, tenantRequiredForSysAdmin: !tenantOptionalForSysAdmin);
                if (!resolution.Success)
                {
                    return FailWithReason(resolution.ErrorMessage ?? "Authorization failed");
                }

                var finalTenantId = resolution.FinalTenantId!;
                var userRoles = resolution.UserRoles!;
                var resolvedUserId = resolution.CanonicalUserId!;

                _logger.LogDebug("Setting tenant context with user ID: {userId}, user type: {userType}, and roles: {roles}",
                    LogSanitizer.RedactUserId(resolvedUserId), UserType.UserToken, LogSanitizer.Sanitize(string.Join(", ", userRoles)));
                _tenantContext.LoggedInUser = resolvedUserId;
                _tenantContext.UserType = UserType.UserToken;
                _tenantContext.TenantId = finalTenantId;
                _tenantContext.UserRoles = userRoles;
                _tenantContext.AuthorizedTenantIds = new[] { finalTenantId };
                _tenantContext.Authorization = null;

                _logger.LogInformation("Successfully authenticated AdminApi connection via ID token: User={UserId}, Tenant={TenantId}, Roles={Roles}",
                    LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(finalTenantId), LogSanitizer.Sanitize(string.Join(", ", userRoles)));

                return AuthenticateResult.Success(BuildTicket(resolvedUserId, finalTenantId));
            }
            catch (TenantNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing X-User-Token for AdminApi Endpoint connection");
                return FailWithReason("Error processing user token for AdminApi Endpoint connection");
            }
        }

        /// <summary>
        /// Builds the claims/identity/principal/ticket shape shared by both authenticated paths
        /// (API-key and ID-token) once each has resolved a canonical user id and final tenant id.
        /// </summary>
        private AuthenticationTicket BuildTicket(string userId, string tenantId)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("TenantId", tenantId)
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return new AuthenticationTicket(principal, Scheme.Name);
        }
    }
}

