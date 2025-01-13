using XiansAi.Server.GenAi;
using XiansAi.Server.MongoDB;
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
    public TenantContext(IConfiguration configuration, ILogger<TenantContext> logger    )
    {
        _configuration = configuration;
        _logger = logger;
    }

    public required string TenantId { get; set; }
    public required string LoggedInUser { get; set; }
    public required IEnumerable<string> AuthorizedTenantIds { get; set; }
    public MongoDBConfig GetMongoDBConfig() { 
        var mongoConfig = _configuration.GetSection($"Tenants:{TenantId}:MongoDB").Get<MongoDBConfig>() 
            ?? throw new InvalidOperationException($"MongoDB configuration for tenant {TenantId} not found");
        
        // if tenant is not using a different server, use the root server
        if (mongoConfig.ConnectionString == null)
        {
            mongoConfig.ConnectionString = _configuration.GetSection("MongoDB:ConnectionString").Value
                ?? throw new InvalidOperationException("MongoDB connection string not found");
        }

        if (mongoConfig.DatabaseName == null)
        {
            mongoConfig.DatabaseName = _configuration.GetSection("MongoDB:DatabaseName").Value
                ?? throw new InvalidOperationException("MongoDB database name not found");
        }

        return mongoConfig;
    }

    public TemporalConfig GetTemporalConfig() { 
        var temporalConfig = _configuration.GetSection($"Tenants:{TenantId}:Temporal").Get<TemporalConfig>();
        if (temporalConfig == null) {
            _logger.LogWarning("Potential New User! Temporal configuration for tenant {TenantId} not found", TenantId);
            // create a new temporal config with default values
            temporalConfig = new TemporalConfig();
        }

        // if tenant is not using a different service account api key, use the root service account api key
        if (temporalConfig.ServiceAccountApiKey == null)
        {
            temporalConfig.ServiceAccountApiKey = _configuration.GetSection("Temporal:ServiceAccountApiKey").Value
                ?? throw new InvalidOperationException("Temporal service account api key not found");
        }

        return temporalConfig;
    }

    public OpenAIConfig GetOpenAIConfig() { 
        return _configuration.GetSection($"Tenants:{TenantId}:OpenAI").Get<OpenAIConfig>() 
            ?? throw new InvalidOperationException($"OpenAI configuration for tenant {TenantId} not found");
    }
}