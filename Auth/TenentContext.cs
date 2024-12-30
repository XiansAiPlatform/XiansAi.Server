using Microsoft.Extensions.Configuration;

public interface ITenantContext
{
    string TenantId { get; }    

    MongoDBConfig GetMongoDBConfig();
    TemporalConfig GetTemporalConfig();
    OpenAIConfig GetOpenAIConfig();
}

public class TenantContext : ITenantContext
{
    private readonly IConfiguration _configuration;

    public TenantContext(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public required string TenantId { get; set; }

    public MongoDBConfig GetMongoDBConfig() { 
        return _configuration.GetSection($"Tenants:{TenantId}:MongoDB").Get<MongoDBConfig>() 
            ?? throw new InvalidOperationException($"MongoDB configuration for tenant {TenantId} not found");
    }

    public TemporalConfig GetTemporalConfig() { 
        return _configuration.GetSection($"Tenants:{TenantId}:Temporal").Get<TemporalConfig>() 
            ?? throw new InvalidOperationException($"Temporal configuration for tenant {TenantId} not found");
    }

    public OpenAIConfig GetOpenAIConfig() { 
        return _configuration.GetSection($"Tenants:{TenantId}:OpenAI").Get<OpenAIConfig>() 
            ?? throw new InvalidOperationException($"OpenAI configuration for tenant {TenantId} not found");
    }
}