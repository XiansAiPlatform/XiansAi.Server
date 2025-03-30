using XiansAi.Server.GenAi;
using XiansAi.Server.Database;
using XiansAi.Server.Temporal;

namespace XiansAi.Server.Auth;

public interface ITenantContext
{
    string TenantId { get; set; }   
    string? LoggedInUser { get; set; }
    IEnumerable<string> AuthorizedTenantIds { get; set; }
    MongoDBConfig GetMongoDBConfig();
    TemporalConfig GetTemporalConfig();
    OpenAIConfig GetOpenAIConfig();
}

public class TenantContext : ITenantContext
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantContext> _logger;

    public required string TenantId { get; set; }
    public string? LoggedInUser { get; set; }
    public IEnumerable<string> AuthorizedTenantIds { get; set; } = new List<string>();

    public TenantContext(IConfiguration configuration, ILogger<TenantContext> logger    )
    {
        _configuration = configuration;
        _logger = logger;
    }

    public MongoDBConfig GetMongoDBConfig() { 
        // ValidateTenantId();

        // get the mongo config for the tenant
        var mongoConfig = _configuration.GetSection($"Tenants:{TenantId}:MongoDB").Get<MongoDBConfig>();

        // if the mongo config is not found, use the default values
        if (mongoConfig == null) {
            _logger.LogInformation("MongoDB configuration for tenant {TenantId} not found. Using default values.", TenantId);

            var connectionString = _configuration.GetSection("MongoDB:ConnectionString").Value
                ?? throw new InvalidOperationException("MongoDB connection string not found");
            var databaseName = TenantId;
            // create a new mongo config with default values
            mongoConfig = new MongoDBConfig {
                ConnectionString = connectionString,
                DatabaseName = databaseName
            };
        } else {
            // if the mongo config is found, use the values
            if (mongoConfig.ConnectionString == null) {
                mongoConfig.ConnectionString = _configuration.GetSection("MongoDB:ConnectionString").Value
                    ?? throw new InvalidOperationException("MongoDB connection string not found");
            }

            if (mongoConfig.DatabaseName == null) {
                mongoConfig.DatabaseName = TenantId;
            }
        }

        return mongoConfig;
    }

    public TemporalConfig GetTemporalConfig() { 
        ValidateTenantId();

        // get the temporal config for the tenant
        var temporalConfig = _configuration.GetSection($"Tenants:{TenantId}:Temporal").Get<TemporalConfig>();

        // we cant share the temporal config between tenants, so if it is not found, throw an error
        if (temporalConfig == null) {
            throw new InvalidOperationException($"Temporal configuration for tenant {TenantId} not found");
        }

        if (temporalConfig.CertificateKeyVaultName == null && temporalConfig.CertificateFilePath == null) 
            throw new InvalidOperationException("CertificateKeyVaultName or CertificateFilePath is required for tenant {TenantId}");
        if (temporalConfig.PrivateKeyKeyVaultName == null && temporalConfig.PrivateKeyFilePath == null) 
            throw new InvalidOperationException("PrivateKeyKeyVaultName or PrivateKeyFilePath is required for tenant {TenantId}");
        if (temporalConfig.FlowServerUrl == null) throw new InvalidOperationException("FlowServerUrl is required for tenant {TenantId}");
        
        // if the flow server namespace is not set, use the tenant id
        if (temporalConfig.FlowServerNamespace == null) {
            temporalConfig.FlowServerNamespace = TenantId;
        }

        return temporalConfig;
    }

    public OpenAIConfig GetOpenAIConfig() { 
        ValidateTenantId();

        var openAIConfig = _configuration.GetSection($"Tenants:{TenantId}:OpenAI").Get<OpenAIConfig>();

        if (openAIConfig == null) {
            // if tenant is not using a different api key, use the root config
            openAIConfig = _configuration.GetSection("OpenAI").Get<OpenAIConfig>() ?? throw new InvalidOperationException("OpenAI configuration not found");
            return openAIConfig;
        } else {
            // if tenant is using a different api key, use the tenant config
            if (openAIConfig.ApiKey == null) {
                openAIConfig.ApiKey = _configuration.GetSection("OpenAI:ApiKey").Value
                    ?? throw new InvalidOperationException("OpenAI api key not found");
            }

            if (openAIConfig.Model == null) {
                openAIConfig.Model = _configuration.GetSection("OpenAI:Model").Value
                    ?? throw new InvalidOperationException("OpenAI model not found");
            }
            return openAIConfig;
        }

    }

    private void ValidateTenantId()
    {
        if (string.IsNullOrEmpty(TenantId)) 
            throw new InvalidOperationException("TenantId is required");
    }
}