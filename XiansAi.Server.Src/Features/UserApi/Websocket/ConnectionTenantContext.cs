using Microsoft.AspNetCore.SignalR;
using Shared.Auth;
using Shared.Utils.Temporal;

namespace Features.UserApi.Websocket
{
    /// <summary>
    /// A copy of the tenant context the authorization handler populated during the SignalR
    /// handshake, carried on the connection so hub methods can read it after the handshake
    /// request is gone.
    ///
    /// It is needed because neither of the two obvious sources survives a hub invocation:
    /// SignalR creates a fresh DI scope per invocation rather than reusing the request scope, so a
    /// scoped <see cref="ITenantContext"/> resolved inside a hub method is a blank instance; and
    /// transports that do not hold the handshake request open — long polling, and reverse proxies
    /// that terminate it — leave <c>IHttpContextAccessor.HttpContext</c> unset or its scope
    /// disposed by the time the method runs.
    /// </summary>
    public sealed class ConnectionTenantContext : ITenantContext
    {
        private const string ConnectionItemKey = "XiansAi.Websocket.TenantContext";

        private ConnectionTenantContext(ITenantContext source)
        {
            UserType = source.UserType;
            TenantId = source.TenantId;
            LoggedInUser = source.LoggedInUser;
            ParticipantId = source.ParticipantId;
            Email = source.Email;
            ProviderSubject = source.ProviderSubject;
            Authorization = source.Authorization;

            // Copied rather than aliased: the source belongs to a request scope that is about to
            // be disposed, and nothing should be able to edit the connection's view of itself.
            UserRoles = source.UserRoles?.ToArray() ?? Array.Empty<string>();
            AuthorizedTenantIds = source.AuthorizedTenantIds?.ToList() ?? new List<string>();
        }

        public UserType UserType { get; set; }
        public string TenantId { get; set; }
        public string LoggedInUser { get; set; }
        public string ParticipantId { get; set; }
        public string? Email { get; set; }
        public string? ProviderSubject { get; set; }
        public string[] UserRoles { get; set; }
        public IEnumerable<string> AuthorizedTenantIds { get; set; }
        public string? Authorization { get; set; }

        /// <summary>
        /// Stores a snapshot of <paramref name="source"/> on the connection, replacing any earlier
        /// one. Call this from <c>OnConnectedAsync</c>, where the handshake context is still alive.
        /// </summary>
        public static void Capture(HubCallerContext connection, ITenantContext source)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(source);

            connection.Items[ConnectionItemKey] = new ConnectionTenantContext(source);
        }

        /// <summary>
        /// Returns the snapshot taken on connect, or null when the connection was established
        /// without one.
        /// </summary>
        public static ConnectionTenantContext? Find(HubCallerContext connection)
        {
            ArgumentNullException.ThrowIfNull(connection);

            return connection.Items.TryGetValue(ConnectionItemKey, out var captured)
                ? captured as ConnectionTenantContext
                : null;
        }

        /// <summary>
        /// Not available on a snapshot: resolving Temporal configuration needs the tenant
        /// configuration repository, which lives in the disposed handshake scope. Hub methods that
        /// need it should resolve a live <see cref="ITenantContext"/> from their own scope.
        /// </summary>
        public Task<TemporalConfig> GetTemporalConfigAsync()
        {
            throw new NotSupportedException(
                "Temporal configuration cannot be resolved from a connection tenant context snapshot.");
        }
    }
}
