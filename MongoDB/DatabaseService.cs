using MongoDB.Driver;
using XiansAi.Server.MongoDB;

public interface IDatabaseService
{
    Task<IMongoDatabase> GetDatabase();
}

public class DatabaseService : IDatabaseService
{
    private readonly IMongoDbClientService _mongoDbClientService;

    public DatabaseService(IMongoDbClientService mongoDbClientService)
    {
        _mongoDbClientService = mongoDbClientService;
    }

    public async Task<IMongoDatabase> GetDatabase()
    {
        return await _mongoDbClientService.GetDatabase();
    }
}
