using MongoDB.Bson;
using XiansAi.Server.Database.Models;
using XiansAi.Server.Database.Repositories;

namespace Features.WebApi.Services.Web;

public class LogsEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<LogsEndpoint> _logger;

    public LogsEndpoint(IDatabaseService databaseService, ILogger<LogsEndpoint> logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task<IResult> GetLogById(string id)
    {
        var logRepository = new LogRepository(await _databaseService.GetDatabase());
        var log = await logRepository.GetByIdAsync(id);
        return Results.Ok(log);
    }

    public async Task<IResult> GetLogsByWorkflowRunId(string workflowRunId, int skip, int limit, int? logLevel = null)
    {
        var logRepository = new LogRepository(await _databaseService.GetDatabase());
        var logs = await logRepository.GetByWorkflowRunIdAsync(workflowRunId, skip, limit, logLevel);
        return Results.Ok(logs);
    }

    public async Task<IResult> GetLogsByDateRange(DateTime startDate, DateTime endDate)
    {
        var logRepository = new LogRepository(await _databaseService.GetDatabase());
        var logs = await logRepository.GetByDateRangeAsync(startDate, endDate);
        return Results.Ok(logs);
    }

    public async Task<IResult> CreateLog(Log log)
    {
        log.Id = ObjectId.GenerateNewId().ToString();
        log.CreatedAt = DateTime.UtcNow;

        var logRepository = new LogRepository(await _databaseService.GetDatabase());
        Console.WriteLine("Creating log: " + log.ToJson());
        await logRepository.CreateAsync(log);
        return Results.Ok(log);
    }

    public async Task<IResult> DeleteLog(string id)
    {
        var logRepository = new LogRepository(await _databaseService.GetDatabase());
        await logRepository.DeleteAsync(id);
        return Results.Ok();
    }

    public async Task<IResult> SearchLogs(string searchTerm)
    {
        var logRepository = new LogRepository(await _databaseService.GetDatabase());
        var logs = await logRepository.SearchAsync(searchTerm);
        return Results.Ok(logs);
    }
}
