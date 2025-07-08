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
    }

    public async Task CreateAsync(Log log)
    {
        await _logs.InsertOneAsync(log);
    }
}
