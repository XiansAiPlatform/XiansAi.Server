using MongoDB.Driver;

namespace Shared.Data;
public interface IDatabaseService
{
    Task<IMongoDatabase> GetDatabaseAsync();
}

public class DatabaseService(IMongoDbClientService mongoDbClientService) : IDatabaseService
{
    public async Task<IMongoDatabase> GetDatabaseAsync()
    {
        return await Task.FromResult(mongoDbClientService.GetDatabase());
    }
}
