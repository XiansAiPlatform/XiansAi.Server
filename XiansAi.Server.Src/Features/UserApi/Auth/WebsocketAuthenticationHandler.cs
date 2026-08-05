using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Data.Models;
using Shared.Utils;
using System.Text.Encodings.Web;

namespace Features.UserApi.Auth
{
    /// <summary>
    /// Authenticates SignalR/WebSocket handshakes for the UserApi (<c>/ws/...</c>).
    ///
    /// This handler owns only what is specific to the WebSocket transport — the feature switch, the
    /// endpoints that refuse JWTs, and where a caller is allowed to put their credential.
    /// Validating the credential is delegated to <see cref="IUserApiCredentialAuthenticator"/>,
    /// which the HTTP handler shares.
    /// </summary>
    public class WebsocketAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string WebsocketPathPrefix = "/ws/";
        private const string TenantChatPath = "/ws/tenant/chat";

        /// <summary>
        /// Query parameters are looked at first, unlike the HTTP handler. Browser WebSocket and
        /// SignalR clients cannot set request headers on the handshake, so the query string is the
        /// primary channel here and reordering would break existing clients that send both.
        /// </summary>
        private static readonly CredentialSource[] LookupOrder =
        [
            CredentialSource.ApiKeyQueryParameter,
            CredentialSource.AccessTokenQueryParameter,
            CredentialSource.AuthorizationHeader
        ];

        private readonly IUserApiCredentialAuthenticator _authenticator;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebsocketAuthenticationHandler> _logger;

        public WebsocketAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration,
            IUserApiCredentialAuthenticator authenticator)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<WebsocketAuthenticationHandler>();
            _configuration = configuration;
            _authenticator = authenticator;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var path = Request.Path.Value ?? string.Empty;
            if (!path.StartsWith(WebsocketPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Let the handlers for the other feature areas process this request.
                return AuthenticateResult.NoResult();
            }

            if (!_configuration.GetSection("WebSockets").GetValue<bool>("Enabled"))
            {
                _logger.LogWarning("WebSockets are not enabled in configuration");
                return AuthenticateResult.Fail("WebSockets are not enabled");
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Processing SignalR/WebSocket request: {Path}", LogSanitizer.Sanitize(path));
            }

            var credential = UserApiCredentialReader.Read(Request, LookupOrder, _logger);
            if (credential == null)
            {
                _logger.LogWarning("No access token found for WebSocket connection");
                return AuthenticateResult.Fail("Invalid credentials");
            }

            // The tenant chat hub is an operator-facing feature reached with a tenant-scoped API
            // key; end-user tokens must not open it.
            var isApiKey = credential.Value.AccessToken.StartsWith(ApiKey.KeyPrefix, StringComparison.Ordinal);
            if (!isApiKey && path.StartsWith(TenantChatPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rejecting non-API-key credential on the tenant chat endpoint");
                return AuthenticateResult.Fail("Invalid credentials");
            }

            var tenantId = Request.Query["tenantId"].ToString();
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
