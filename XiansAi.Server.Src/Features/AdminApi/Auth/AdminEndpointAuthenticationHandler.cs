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

        /// <summary>
        /// Optional second credential carrying the real acting human's own OIDC token — any
        /// AdminApi client may forward one (agent-studio's <c>session.accessToken</c> is today's
        /// only example). Kept separate from <c>Authorization</c>, which stays the API key.
        /// Validated by <see cref="IAdminActingUserResolver"/>
        /// </summary>
        public const string UserTokenHeaderName = "X-User-Token";

        private readonly ITenantContext _tenantContext;
        private readonly ILogger<AdminEndpointAuthenticationHandler> _logger;
        private readonly IApiKeyService _apiKeyService;
        private readonly IAdminRoleTenantResolver _adminRoleTenantResolver;
        private readonly IAdminActingUserResolver _actingUserResolver;

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
            IAdminActingUserResolver actingUserResolver)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<AdminEndpointAuthenticationHandler>();
            _tenantContext = tenantContext;
            _apiKeyService = apiKeyService;
            _adminRoleTenantResolver = adminRoleTenantResolver;
            _actingUserResolver = actingUserResolver;
        }

        /// <summary>
        /// Applies the optional forwarded user token, when present, upgrading
        /// <c>ITenantContext.LoggedInUser</c>/<c>UserRoles</c> from the API key owner to the real
        /// verified human. A present-but-invalid token fails the whole request rather than silently
        /// falling back — see the "fail closed, not silent fallback" decision in the design doc: a
        /// caller that tried to prove who it is and failed must not quietly regain the key's full
        /// privilege under the untouched fallback path.
        /// </summary>
        private async Task<AuthenticateResult?> TryApplyVerifiedActingUserAsync(string finalTenantId, string apiKeyOwnerUserId)
        {
            var userToken = Request.Headers[UserTokenHeaderName].FirstOrDefault();
            var resolution = await _actingUserResolver.ResolveAsync(userToken, finalTenantId);

            if (!resolution.Attempted)
            {
                return null; // no token supplied - proceed with the key-owner identity, unchanged
            }

            if (!resolution.Success)
            {
                return FailWithReason(resolution.Error ?? "Invalid user token");
            }

            _tenantContext.ServiceCallerUserId = apiKeyOwnerUserId;
            _tenantContext.LoggedInUser = resolution.CanonicalUserId!;
            _tenantContext.UserRoles = resolution.UserRoles!;
            _tenantContext.ActingUserVerified = true;

            return null; // success - _tenantContext is already updated; caller proceeds as normal
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
                        _tenantContext.ServiceCallerUserId = resolvedUserId;
                        _tenantContext.ActingUserVerified = false;

                        // Optional second credential: upgrades LoggedInUser/UserRoles from the API
                        // key owner to the real acting human when the caller forwards one. Absent
                        // or already-invalid tokens leave everything above untouched (or fail the
                        // request outright for an invalid one) — see the method's own doc comment.
                        var userTokenFailure = await TryApplyVerifiedActingUserAsync(finalTenantId, resolvedUserId);
                        if (userTokenFailure != null)
                        {
                            return userTokenFailure;
                        }

                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.NameIdentifier, _tenantContext.LoggedInUser),
                            new Claim("TenantId", finalTenantId)
                        };

                        var identity = new ClaimsIdentity(claims, Scheme.Name);
                        var principal = new ClaimsPrincipal(identity);
                        var ticket = new AuthenticationTicket(principal, Scheme.Name);
                        _logger.LogInformation("Successfully authenticated AdminApi connection: User={UserId}, ServiceCaller={ServiceCallerUserId}, Tenant={TenantId}, Roles={Roles}, Verified={Verified}",
                            LogSanitizer.RedactUserId(_tenantContext.LoggedInUser), LogSanitizer.RedactUserId(resolvedUserId),
                            LogSanitizer.Sanitize(finalTenantId), LogSanitizer.Sanitize(string.Join(", ", _tenantContext.UserRoles)),
                            _tenantContext.ActingUserVerified);

                        return AuthenticateResult.Success(ticket);
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
                    _logger.LogWarning("No access token found for AdminApi Endpoint connection");
                    return FailWithReason("No access token found for AdminApi Endpoint connection");
                }
            }
            else
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                return FailWithReason("Failed to resolve ITenantContext from request scope");
            }
        }

    }
}

