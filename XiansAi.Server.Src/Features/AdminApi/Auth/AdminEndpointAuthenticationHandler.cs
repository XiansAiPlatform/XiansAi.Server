using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Exceptions;
using Shared.Services;
using Shared.Utils;

namespace Features.AdminApi.Auth;

public class AdminEndpointAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// HttpContext.Items key used to surface the specific authentication failure reason
    /// to the authorization result handler, so the 401 response carries a meaningful
    /// message instead of a generic one.
    /// </summary>
    public const string FailureReasonItemKey = "AdminApi.AuthFailureReason";

    private const string BearerPrefix = "Bearer ";
    private const string ApiKeyPrefix = "sk-Xnai-";
    private const string TenantIdHeader = "X-Tenant-Id";

    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminEndpointAuthenticationHandler> _logger;
    private readonly IApiKeyService _apiKeyService;
    private readonly IAdminRoleTenantResolver _adminRoleTenantResolver;

    public AdminEndpointAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITenantContext tenantContext,
        IApiKeyService apiKeyService,
        IAdminRoleTenantResolver adminRoleTenantResolver)
        : base(options, logger, encoder)
    {
        _logger = logger.CreateLogger<AdminEndpointAuthenticationHandler>();
        _tenantContext = tenantContext;
        _apiKeyService = apiKeyService;
        _adminRoleTenantResolver = adminRoleTenantResolver;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!IsAdminApiRequest())
        {
            _logger.LogDebug(
                "Skipping admin endpoint authentication for non-AdminApi path: {Path}",
                LogSanitizer.Sanitize(Request.Path));
            return AuthenticateResult.NoResult();
        }

        _logger.LogDebug(
            "Processing AdminApi endpoint request: {Path}",
            LogSanitizer.Sanitize(Request.Path));

        var accessToken = ExtractBearerToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("No access token found for AdminApi Endpoint connection");
            return FailWithReason("No access token found for AdminApi Endpoint connection");
        }

        try
        {
            return await AuthenticateWithApiKeyAsync(accessToken, ExtractTenantId());
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

    private bool IsAdminApiRequest()
    {
        var path = Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        if (!path.StartsWith("/api/", StringComparison.Ordinal) || !path.Contains("/admin", StringComparison.Ordinal))
        {
            return false;
        }

        // Match /api/{version}/admin/... (e.g. /api/v1/admin)
        var pathParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return pathParts.Length >= 3
            && pathParts[0] == "api"
            && pathParts[2] == "admin";
    }

    private string? ExtractBearerToken()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authHeader[BearerPrefix.Length..].Trim();
    }

    /// <summary>
    /// Tenant ID sources in priority order:
    /// 1. Query parameter (tenantId=)
    /// 2. Route parameter (e.g. /tenants/{tenantId})
    /// 3. X-Tenant-Id header
    /// Optional — when omitted, derived from the API key to avoid IDOR.
    /// </summary>
    private string ExtractTenantId()
    {
        var tenantId = Request.Query["tenantId"].ToString();
        if (!string.IsNullOrEmpty(tenantId))
        {
            return tenantId;
        }

        if (Request.RouteValues.TryGetValue("tenantId", out var routeTenantId) && routeTenantId != null)
        {
            return routeTenantId.ToString() ?? string.Empty;
        }

        return Request.Headers[TenantIdHeader].FirstOrDefault() ?? string.Empty;
    }

    private async Task<AuthenticateResult> AuthenticateWithApiKeyAsync(string accessToken, string tenantIdFromRequest)
    {
        if (!accessToken.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
        {
            _logger.LogWarning("Invalid API key format. API key must start with '{ApiKeyPrefix}'", ApiKeyPrefix);
            return FailWithReason("Invalid API key format");
        }

        // Look up by token first so SysAdmins (Tenant=System) can access other tenants.
        var apiKey = await _apiKeyService.GetApiKeyByRawKeyAsync(accessToken);
        if (apiKey == null)
        {
            _logger.LogWarning("Invalid API key submitted");
            return FailWithReason("Invalid API key");
        }

        var resolutionResult = await _adminRoleTenantResolver.ResolveAsync(
            apiKey.CreatedBy, apiKey, tenantIdFromRequest);

        if (!resolutionResult.Success)
        {
            return FailWithReason(resolutionResult.ErrorMessage ?? "Authorization failed");
        }

        return CreateSuccessTicket(apiKey, accessToken, resolutionResult);
    }

    private AuthenticateResult CreateSuccessTicket(
        ApiKey apiKey,
        string accessToken,
        AdminRoleTenantResolutionResult resolutionResult)
    {
        var finalTenantId = resolutionResult.FinalTenantId!;
        var userRoles = resolutionResult.UserRoles!;

        _logger.LogDebug(
            "Setting tenant context with user ID: {UserId}, user type: {UserType}, and roles: {Roles}",
            apiKey.CreatedBy,
            UserType.UserApiKey,
            string.Join(", ", userRoles));

        _tenantContext.LoggedInUser = apiKey.CreatedBy;
        _tenantContext.UserType = UserType.UserApiKey;
        _tenantContext.TenantId = finalTenantId;
        _tenantContext.UserRoles = userRoles.ToArray();
        _tenantContext.AuthorizedTenantIds = [finalTenantId];
        _tenantContext.Authorization = accessToken;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.CreatedBy),
            new("TenantId", finalTenantId)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        _logger.LogInformation(
            "Successfully authenticated AdminApi connection: User={UserId}, Tenant={TenantId}, Roles={Roles}",
            LogSanitizer.Sanitize(apiKey.CreatedBy),
            LogSanitizer.Sanitize(finalTenantId),
            LogSanitizer.Sanitize(string.Join(", ", userRoles)));

        return AuthenticateResult.Success(ticket);
    }

    private AuthenticateResult FailWithReason(string reason)
    {
        Context.Items[FailureReasonItemKey] = reason;
        return AuthenticateResult.Fail(reason);
    }
}
