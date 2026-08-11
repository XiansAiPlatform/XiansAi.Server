using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Utils;
using System.Text.Encodings.Web;

namespace Features.UserApi.Auth
{
    /// <summary>
    /// Authenticates HTTP requests to the UserApi (<c>/api/user/...</c>).
    ///
    /// This handler owns only what is specific to the HTTP transport — deciding whether the path is
    /// ours, and where a caller is allowed to put their credential. Validating the credential is
    /// delegated to <see cref="IUserApiCredentialAuthenticator"/>, which the WebSocket handler
    /// shares.
    ///
    /// Rate limiting runs ahead of authentication in middleware; every UserApi endpoint should use
    /// <c>.WithAgentUserApiRateLimit()</c> so that credential guessing is throttled.
    /// </summary>
    public class EndpointAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string UserApiPathPrefix = "/api/user/";

        /// <summary>
        /// The Authorization header is looked at first so that a client which has adopted the
        /// secure pattern is never silently overridden by a stale query parameter it forgot to
        /// stop sending.
        /// </summary>
        private static readonly CredentialSource[] LookupOrder =
        [
            CredentialSource.AuthorizationHeader,
            CredentialSource.ApiKeyQueryParameter,
            CredentialSource.AccessTokenQueryParameter
        ];

        private readonly IUserApiCredentialAuthenticator _authenticator;
        private readonly ILogger<EndpointAuthenticationHandler> _logger;

        public EndpointAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IUserApiCredentialAuthenticator authenticator)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<EndpointAuthenticationHandler>();
            _authenticator = authenticator;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var path = Request.Path.Value ?? string.Empty;
            if (!path.StartsWith(UserApiPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Let the handlers for the other feature areas process this request.
                return AuthenticateResult.NoResult();
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Processing UserApi endpoint request: {Path}", LogSanitizer.Sanitize(path));
            }

            // tenantId is optional for API keys, where the tenant is derived from the key itself,
            // and required for JWTs, where it selects which OIDC rules apply.
            var tenantId = Request.Query["tenantId"].ToString();

            var apiKeyId = Request.Query["apikeyId"].ToString();
            if (!string.IsNullOrEmpty(apiKeyId))
            {
                // apikeyId is itself a credential — anyone holding it can authenticate as the
                // associated user — so it carries the same query-string leak risks as ?apikey= and
                // gets the same deprecation warning.
                UserApiCredentialReader.WarnQueryStringCredentialDeprecated(Request, _logger, "apikeyId");
                return await _authenticator.AuthenticateByApiKeyIdAsync(apiKeyId, Scheme.Name);
            }

            var credential = UserApiCredentialReader.Read(Request, LookupOrder, _logger);
            if (credential == null)
            {
                _logger.LogWarning("No access token found for UserApi endpoint request");
                return AuthenticateResult.Fail("Invalid credentials");
            }

            return await _authenticator.AuthenticateAsync(credential.Value, tenantId, Scheme.Name);
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }
    }
}
