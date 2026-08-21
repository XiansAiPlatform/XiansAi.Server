using Shared.Utils.Temporal;

namespace Shared.Auth;

    public enum UserType
    {
        DevToken,
        UserToken,
        UserApiKey,
        AgentApiKey,
        Unknown
    }

    public interface ITenantContext
    {
        UserType UserType { get; set; }
        string TenantId { get; set; }   
        string LoggedInUser { get; set; }

        /// <summary>
        /// Identifies the caller as a conversation participant, which is the key conversation
        /// threads are stored under. This is deliberately separate from <see cref="LoggedInUser"/>:
        /// the UserApi JWT paths set the latter to the canonical `provider|subject` id for claims
        /// and display, while threads are keyed on the raw provider subject (or the account email
        /// when that is preferred for conversation continuity). Defaults to
        /// <see cref="LoggedInUser"/> for the flows where the two are the same.
        /// </summary>
        string ParticipantId { get; set; }

        /// <summary>
        /// The account's stored email when known. A token holder may name themselves by it, which
        /// resolves to their <see cref="ParticipantId"/>.
        ///
        /// It identifies the caller but must not namespace anything: another account can hold the
        /// same address, in which case <see cref="ParticipantId"/> is deliberately something else.
        /// </summary>
        string? Email { get; set; }

        /// <summary>
        /// The raw provider subject from the token. Kept so clients that pass the JWT <c>sub</c> as
        /// participant id remain authorized when conversation identity prefers email.
        /// </summary>
        string? ProviderSubject { get; set; }

        string[] UserRoles { get; set; }
        IEnumerable<string> AuthorizedTenantIds { get; set; }
        
        TemporalConfig GetTemporalConfig();

        string? Authorization { get; set; }
    }

    public class TenantContext : ITenantContext
    {
        private readonly IConfiguration _configuration;
        private string? _participantId;

        public UserType UserType { get; set; } = UserType.Unknown;
        public required string TenantId { get; set; }
        public required string LoggedInUser { get; set; }

        public string ParticipantId
        {
            get => _participantId ?? LoggedInUser;
            set => _participantId = value;
        }

        public string? Email { get; set; }
        public string? ProviderSubject { get; set; }

        public required string[] UserRoles { get; set; } = Array.Empty<string>();
        public IEnumerable<string> AuthorizedTenantIds { get; set; } = new List<string>();
        public string? Authorization { get; set; }
        public TenantContext(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public TemporalConfig GetTemporalConfig() 
        { 
            if (string.IsNullOrEmpty(TenantId)) 
                 throw new InvalidOperationException("TenantId is required");

            // get the temporal config for the tenant
            var temporalConfig = _configuration.GetSection($"Tenants:{TenantId}:Temporal").Get<TemporalConfig>();

            if (temporalConfig == null) {
                // fallback to the root temporal config
                temporalConfig = _configuration.GetSection("Temporal").Get<TemporalConfig>();
            }
            // we cant share the temporal config between tenants, so if it is not found, throw an error
            if (temporalConfig == null) {
                throw new InvalidOperationException($"Temporal configuration for tenant {TenantId} not found");
            }
            if (temporalConfig.FlowServerUrl == null) 
                throw new InvalidOperationException($"FlowServerUrl is required for tenant {TenantId}");
            
            return temporalConfig;
        }
     }
