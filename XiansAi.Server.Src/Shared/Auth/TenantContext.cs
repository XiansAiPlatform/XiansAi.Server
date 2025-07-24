using Shared.Utils.Temporal;

namespace Shared.Auth;

    public interface ITenantContext
    {
        string TenantId { get; set; }   
        string LoggedInUser { get; set; }
        string[] UserRoles { get; set; }
        IEnumerable<string> AuthorizedTenantIds { get; set; }
        
        TemporalConfig GetTemporalConfig();

        string? Authorization { get; set; }
    }

    public class TenantContext : ITenantContext
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<TenantContext> _logger;
 
        public required string TenantId { get; set; }
        public required string LoggedInUser { get; set; }
        public required string[] UserRoles { get; set; } = Array.Empty<string>();
        public IEnumerable<string> AuthorizedTenantIds { get; set; } = new List<string>();
        public string? Authorization { get; set; }
        public TenantContext(IConfiguration configuration, ILogger<TenantContext> logger)
        {
            _configuration = configuration;
            _logger = logger;
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

            // if (string.IsNullOrEmpty(temporalConfig.CertificateBase64)) 
            //     throw new InvalidOperationException($"CertificateBase64 is required for tenant {TenantId}");
            // if (string.IsNullOrEmpty(temporalConfig.PrivateKeyBase64)) 
            //     throw new InvalidOperationException($"PrivateKeyBase64 is required for tenant {TenantId}");
            if (temporalConfig.FlowServerUrl == null) 
                throw new InvalidOperationException($"FlowServerUrl is required for tenant {TenantId}");
            
            return temporalConfig;
        }
     }
