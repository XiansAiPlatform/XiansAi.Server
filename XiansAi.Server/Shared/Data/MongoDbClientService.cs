using MongoDB.Driver;
using System.Collections.Concurrent;
using Features.Shared.Auth;

namespace XiansAi.Server.Database;

public interface IMongoDbClientService
{
    IMongoDatabase GetDatabase();
    IMongoCollection<T> GetCollection<T>(string collectionName);
    IMongoClient GetClient();
}

public class MongoDbClientService : IMongoDbClientService
{
    private static readonly ConcurrentDictionary<string, IMongoClient> _mongoClients = new();
    public MongoDBConfig Config { get; init; }
    private readonly ITenantContext _tenantContext;


    public MongoDbClientService(MongoDBConfig config, ITenantContext tenantContext)
    {
        Config = config;
        _tenantContext = tenantContext;
    }

    public IMongoDatabase GetDatabase()
    {
        var mongoClient = GetClient();
        return mongoClient.GetDatabase(Config.DatabaseName);
    }

    public IMongoCollection<T> GetCollection<T>(string collectionName)
    {
        var database = GetDatabase();
        return database.GetCollection<T>(collectionName);
    }

    public IMongoClient GetClient()
    {
        var tenantId = _tenantContext.TenantId;
        return _mongoClients.GetOrAdd(tenantId, _ => 
        {
            var connectionString = Config.ConnectionString;
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            return new MongoClient(settings);
        });
    }

}

