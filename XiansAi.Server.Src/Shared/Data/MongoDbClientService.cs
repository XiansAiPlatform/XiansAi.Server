using MongoDB.Driver;
using System.Collections.Concurrent;
using Shared.Auth;

namespace XiansAi.Server.Database;

public interface IMongoDbClientService
{
    IMongoDatabase GetDatabase();
    IMongoCollection<T> GetCollection<T>(string collectionName);
    IMongoClient GetClient();
}

public class MongoDbClientService : IMongoDbClientService
{
    public IMongoDBConfig Config { get; init; }

    public MongoDbClientService(IMongoDbContext context)
    {
        Config = context.GetMongoDBConfig();
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
        var connectionString = Config.ConnectionString;
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        return new MongoClient(settings);
    }

}

