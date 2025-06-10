using MongoDB.Driver;
using Shared.Data;
using XiansAi.Server.Features.AgentApi.Models;

namespace XiansAi.Server.Features.AgentApi.Repositories;

public interface ILogRepository
{
    Task CreateAsync(Log log);
}

public class LogRepository : ILogRepository
{
    private readonly IMongoCollection<Log> _logs;

    public LogRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _logs = database.GetCollection<Log>("logs");
        
        // Create TTL index for automatic log expiration
        CreateTtlIndexIfNotExists();
    }

    private void CreateTtlIndexIfNotExists()
    {
        try
        {
            // Create TTL index on CreatedAt field to expire logs after 30 days
            // You can adjust the expiration time by changing the ExpireAfter value
            var indexKeys = Builders<Log>.IndexKeys.Ascending(x => x.CreatedAt);
            var indexOptions = new CreateIndexOptions 
            { 
                ExpireAfter = TimeSpan.FromDays(30), // 30 days
                Background = true,
                Name = "logs_ttl_created_at"
            };
            var indexModel = new CreateIndexModel<Log>(indexKeys, indexOptions);
            
            _logs.Indexes.CreateOne(indexModel);
        }
        catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict")
        {
            // Index already exists with different options, that's fine
            Console.WriteLine("TTL index already exists on logs collection");
        }
        catch (Exception ex)
        {
            // Log the error but don't throw - we want the application to continue even if index creation fails
            Console.WriteLine($"Warning: Failed to create TTL index on logs collection: {ex.Message}");
        }
    }

    public async Task CreateAsync(Log log)
    {
        await _logs.InsertOneAsync(log);
    }
}
