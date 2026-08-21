using Microsoft.AspNetCore.Authentication;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils;
using System.Security.Claims;

namespace Features.UserApi.Auth;

/// <summary>
/// Where a UserApi caller may present their credential.
///
/// Only <see cref="AuthorizationHeader"/> is safe: query strings leak into reverse-proxy access
/// logs, CDN logs, browser history and Referer headers. The two query parameters remain supported
/// because browser <c>EventSource</c> and some SignalR clients cannot set headers.
/// </summary>
public enum CredentialSource
{
    AuthorizationHeader,
    ApiKeyQueryParameter,
    AccessTokenQueryParameter
}

/// <summary>
/// A credential lifted out of a request, along with where the caller put it.
/// </summary>
public readonly record struct PresentedCredential(string AccessToken, CredentialSource Source)
{
    public bool IsFromAuthorizationHeader => Source == CredentialSource.AuthorizationHeader;
}

/// <summary>
/// Reads the credential a caller presented, looking in a handler-supplied order.
/// </summary>
public static class UserApiCredentialReader
{
    private const string ApiKeyQueryParameter = "apikey";
    private const string AccessTokenQueryParameter = "access_token";

    /// <summary>
    /// Returns the first credential found in <paramref name="lookupOrder"/>, or null when the
    /// caller presented none. A credential found in the query string is logged as deprecated.
    /// </summary>
    public static PresentedCredential? Read(
        HttpRequest request,
        IReadOnlyList<CredentialSource> lookupOrder,
        ILogger logger)
    {
        foreach (var source in lookupOrder)
        {
            var token = ReadFrom(request, source);
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            if (source != CredentialSource.AuthorizationHeader)
            {
                WarnQueryStringCredentialDeprecated(request, logger, QueryParameterNameOf(source));
            }

            return new PresentedCredential(token, source);
        }

        return null;
    }

    /// <summary>
    /// Logs a one-line deprecation warning when a caller authenticates via a query-string
    /// credential. The token VALUE is intentionally never included — query strings already leak
    /// into reverse-proxy access logs, CDN logs, browser history and Referer headers, and we don't
    /// want to amplify the exposure by writing it into application logs too.
    /// </summary>
    public static void WarnQueryStringCredentialDeprecated(
        HttpRequest request,
        ILogger logger,
        string parameterName)
    {
        logger.LogWarning(
            "Credential supplied via query parameter '?{Parameter}=' on {Method} {Path} — DEPRECATED. " +
            "Send 'Authorization: Bearer <token>' instead. Query-string credentials leak into " +
            "reverse-proxy access logs, CDN logs, browser history, and Referer headers.",
            LogSanitizer.Sanitize(parameterName),
            LogSanitizer.Sanitize(request.Method),
            LogSanitizer.Sanitize(request.Path));
    }

    private static string? ReadFrom(HttpRequest request, CredentialSource source)
    {
        switch (source)
        {
            case CredentialSource.AuthorizationHeader:
                var authHeader = request.Headers.Authorization.FirstOrDefault();
                var (extracted, token) = AuthorizationHeaderHelper.ExtractBearerToken(authHeader);
                return extracted ? token : null;

            case CredentialSource.ApiKeyQueryParameter:
                return request.Query[ApiKeyQueryParameter].ToString();

            case CredentialSource.AccessTokenQueryParameter:
                return request.Query[AccessTokenQueryParameter].ToString();

            default:
                return null;
        }
    }

    private static string QueryParameterNameOf(CredentialSource source) => source switch
    {
        CredentialSource.ApiKeyQueryParameter => ApiKeyQueryParameter,
        CredentialSource.AccessTokenQueryParameter => AccessTokenQueryParameter,
        _ => source.ToString()
    };
}

/// <summary>
/// Authenticates a credential presented to the UserApi and turns it into a claims principal.
/// </summary>
public interface IUserApiCredentialAuthenticator
{
    /// <summary>
    /// Authenticates an API key or a JWT. <paramref name="requestedTenantId"/> is the
    /// caller-supplied tenant: required for JWTs, because it selects which OIDC rules apply, and
    /// optional for API keys, where the tenant is derived from the key itself.
    /// </summary>
    Task<AuthenticateResult> AuthenticateAsync(
        PresentedCredential credential,
        string? requestedTenantId,
        string schemeName);

    /// <summary>
    /// Authenticates via the deprecated <c>?apikeyId=</c> parameter, which names an API key record
    /// rather than presenting the key itself.
    /// </summary>
    Task<AuthenticateResult> AuthenticateByApiKeyIdAsync(string apiKeyId, string schemeName);
}

/// <summary>
/// The credential handling shared by the UserApi HTTP and WebSocket authentication handlers.
///
/// Those two entry points differ only in where a caller may put the credential and in a few
/// transport-specific gates. Everything from "here is a token" onwards — telling an API key from a
/// JWT, validating it, checking tenant membership, populating the tenant context and building the
/// ticket — is identical, and lives here so that the two cannot drift apart. They previously held
/// separate copies that had already diverged.
///
/// Failures return a deliberately vague reason. The specific cause is logged instead, so that a
/// caller probing the API cannot use the response to discover which tenants exist or how their
/// OIDC rules are configured.
/// </summary>
public class UserApiCredentialAuthenticator : IUserApiCredentialAuthenticator
{
    private const string GenericFailureReason = "Invalid credentials";

    private readonly ITenantContext _tenantContext;
    private readonly IApiKeyService _apiKeyService;
    private readonly IDynamicOidcValidator _oidcValidator;
    private readonly IAuthorizedTenantResolver _authorizedTenantResolver;
    private readonly ILogger<UserApiCredentialAuthenticator> _logger;

    public UserApiCredentialAuthenticator(
        ITenantContext tenantContext,
        IApiKeyService apiKeyService,
        IDynamicOidcValidator oidcValidator,
        IAuthorizedTenantResolver authorizedTenantResolver,
        ILogger<UserApiCredentialAuthenticator> logger)
    {
        _tenantContext = tenantContext;
        _apiKeyService = apiKeyService;
        _oidcValidator = oidcValidator;
        _authorizedTenantResolver = authorizedTenantResolver;
        _logger = logger;
    }

    public async Task<AuthenticateResult> AuthenticateAsync(
        PresentedCredential credential,
        string? requestedTenantId,
        string schemeName)
    {
        try
        {
            if (credential.AccessToken.StartsWith(ApiKey.KeyPrefix, StringComparison.Ordinal))
            {
                // An API key is a long-lived tenant-scoped secret, so it is only passed on to
                // agents when the caller put it in the Authorization header. One sent in the query
                // string stays here.
                if (credential.IsFromAuthorizationHeader)
                {
                    _tenantContext.Authorization = credential.AccessToken;
                }

                return await AuthenticateApiKeyAsync(credential.AccessToken, requestedTenantId, schemeName);
            }

            if (LooksLikeJwt(credential.AccessToken))
            {
                return await AuthenticateJwtAsync(credential.AccessToken, requestedTenantId, schemeName);
            }

            _logger.LogWarning("Credential is neither an API key nor a JWT");
            return AuthenticateResult.Fail(GenericFailureReason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing UserApi credential");
            return AuthenticateResult.Fail(GenericFailureReason);
        }
    }

    public async Task<AuthenticateResult> AuthenticateByApiKeyIdAsync(string apiKeyId, string schemeName)
    {
        try
        {
            var apiKey = await _apiKeyService.GetApiKeyByIdAsync(apiKeyId);
            if (apiKey == null)
            {
                _logger.LogWarning("Invalid apikeyId submitted");
                return AuthenticateResult.Fail(GenericFailureReason);
            }

            return SucceedAsApiKey(apiKey, schemeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing apikeyId");
            return AuthenticateResult.Fail(GenericFailureReason);
        }
    }

    /// <summary>
    /// Authenticates an API key. When the caller supplies no tenant it is derived from the key,
    /// which is what keeps a caller from naming someone else's tenant; when they do supply one
    /// (legacy clients) it has to be the key's own tenant.
    /// </summary>
    private async Task<AuthenticateResult> AuthenticateApiKeyAsync(
        string rawKey,
        string? requestedTenantId,
        string schemeName)
    {
        if (string.IsNullOrEmpty(requestedTenantId))
        {
            var apiKey = await _apiKeyService.GetApiKeyByRawKeyAsync(rawKey);
            if (apiKey == null)
            {
                _logger.LogWarning("Invalid API key submitted");
                return AuthenticateResult.Fail(GenericFailureReason);
            }

            return SucceedAsApiKey(apiKey, schemeName);
        }

        var tenantScopedKey = await _apiKeyService.GetApiKeyByRawKeyAsync(rawKey, requestedTenantId);
        if (tenantScopedKey == null || tenantScopedKey.TenantId != requestedTenantId)
        {
            _logger.LogWarning("API key does not match provided tenant {TenantId}",
                LogSanitizer.Sanitize(requestedTenantId));
            return AuthenticateResult.Fail(GenericFailureReason);
        }

        return SucceedAsApiKey(tenantScopedKey, schemeName);
    }

    private async Task<AuthenticateResult> AuthenticateJwtAsync(
        string token,
        string? requestedTenantId,
        string schemeName)
    {
        // Unlike an API key, a JWT carries no tenant of its own — the tenant selects which OIDC
        // rules the token is validated against, so it has to be supplied.
        if (string.IsNullOrEmpty(requestedTenantId))
        {
            _logger.LogWarning("JWT authentication requires a tenantId parameter");
            return AuthenticateResult.Fail(GenericFailureReason);
        }

        var validation = await _oidcValidator.ValidateAsync(requestedTenantId, token);
        if (!validation.Success ||
            string.IsNullOrEmpty(validation.CanonicalUserId) ||
            string.IsNullOrEmpty(validation.ProviderUserId))
        {
            _logger.LogWarning("JWT validation failed: {Error}", LogSanitizer.Sanitize(validation.Error));
            return AuthenticateResult.Fail(GenericFailureReason);
        }

        // A valid token proves who the caller is, not which tenant they may act as, so the
        // caller-supplied tenant is checked against the tenants this user is an approved member of.
        var resolution = await _authorizedTenantResolver.ResolveAsync(validation, requestedTenantId);
        if (!resolution.IsAuthorized)
        {
            _logger.LogWarning("Tenant {TenantId} is not authorized for the authenticated user",
                LogSanitizer.Sanitize(requestedTenantId));
            return AuthenticateResult.Fail(GenericFailureReason);
        }

        var resolvedTenantId = resolution.MatchedTenantId!;
        var authorizedTenantIds = resolution.AuthorizedTenantIds;

        // Records written by this path are attributed to the canonical `provider|subject` id.
        var canonicalUserId = validation.CanonicalUserId!;

        // Two addresses with different jobs. The conversation one namespaces message threads, so it
        // is only present when it names a single account; the token subject stands in otherwise.
        // The account one merely identifies the caller, so it is set either way and lets someone
        // whose address is shared still be recognised when they name themselves by it.
        var conversationEmail = Normalize(resolution.ConversationEmail);
        var accountEmail = Normalize(resolution.AccountEmail);
        var providerSubject = validation.ProviderUserId;
        var participantId = conversationEmail ?? providerSubject;

        _tenantContext.LoggedInUser = canonicalUserId;
        _tenantContext.UserType = UserType.UserToken;
        _tenantContext.ParticipantId = participantId;
        _tenantContext.Email = accountEmail;
        _tenantContext.ProviderSubject = providerSubject;
        _tenantContext.TenantId = resolvedTenantId;
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds;

        // The caller's own token, forwarded so that agents acting on this request can call back as
        // them. Unlike an API key this is short-lived and scoped to the user, so it goes on
        // regardless of which parameter the caller used — browser EventSource clients can only
        // send it in the query string.
        _tenantContext.Authorization = token;

        var claims = new List<Claim>(6 + authorizedTenantIds.Count)
        {
            new(ClaimTypes.NameIdentifier, canonicalUserId),
            new(UserApiClaimTypes.TenantId, resolvedTenantId),
            new(UserApiClaimTypes.ParticipantId, participantId),
            new(UserApiClaimTypes.UserType, nameof(UserType.UserToken)),
            new(UserApiClaimTypes.ProviderSubject, providerSubject)
        };

        if (accountEmail != null)
        {
            claims.Add(new Claim(UserApiClaimTypes.Email, accountEmail));
        }

        foreach (var tenantId in authorizedTenantIds)
        {
            claims.Add(new Claim(UserApiClaimTypes.AuthorizedTenantId, tenantId));
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Authenticated JWT for user {UserId} on tenant {TenantId}, authorized for {TenantCount} tenant(s)",
                LogSanitizer.Sanitize(canonicalUserId),
                LogSanitizer.Sanitize(resolvedTenantId),
                authorizedTenantIds.Count);
        }

        return Succeed(claims, schemeName);
    }

    private static string? Normalize(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    private AuthenticateResult SucceedAsApiKey(ApiKey apiKey, string schemeName)
    {
        _tenantContext.LoggedInUser = apiKey.CreatedBy;
        _tenantContext.UserType = UserType.UserApiKey;
        _tenantContext.TenantId = apiKey.TenantId;
        _tenantContext.AuthorizedTenantIds = new[] { apiKey.TenantId };

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.CreatedBy),
            new(UserApiClaimTypes.TenantId, apiKey.TenantId),
            new(UserApiClaimTypes.UserType, nameof(UserType.UserApiKey))
        };

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Authenticated API key for user {UserId} on tenant {TenantId}",
                LogSanitizer.Sanitize(apiKey.CreatedBy), LogSanitizer.Sanitize(apiKey.TenantId));
        }

        return Succeed(claims, schemeName);
    }

    private static AuthenticateResult Succeed(List<Claim> claims, string schemeName)
    {
        var identity = new ClaimsIdentity(claims, schemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, schemeName));
    }

    /// <summary>
    /// A compact JWS has exactly two dots. Counted directly rather than via LINQ because this runs
    /// on every request.
    /// </summary>
    private static bool LooksLikeJwt(string token)
    {
        var dots = 0;
        foreach (var c in token)
        {
            if (c == '.' && ++dots > 2)
            {
                return false;
            }
        }

        return dots == 2;
    }
}
