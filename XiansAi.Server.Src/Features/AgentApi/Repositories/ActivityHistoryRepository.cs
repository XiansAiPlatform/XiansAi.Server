using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Data;
using Shared.Services;

namespace Features.AgentApi.Repositories;

public interface IActivityHistoryRepository
{
    void CreateWithoutWaiting(ActivityHistory activity);
}

public class ActivityHistoryRepository : IActivityHistoryRepository
{
    private readonly IMongoCollection<ActivityHistory> _activities;
    private readonly IBackgroundTaskService _backgroundTaskService;
    private readonly ILogger<ActivityHistoryRepository> _logger;

    public ActivityHistoryRepository(
        IDatabaseService databaseService, 
        IBackgroundTaskService backgroundTaskService,
        ILogger<ActivityHistoryRepository> logger)
    {
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
        _activities = database.GetCollection<ActivityHistory>("activity_history");
        _backgroundTaskService = backgroundTaskService;
        _logger = logger;
    }

    // Use this method when you don't need to wait for the operation to complete
    public void CreateWithoutWaiting(ActivityHistory activity)
    {
        _backgroundTaskService.QueueDatabaseOperation(async () => 
            await _activities.InsertOneAsync(activity));
    }
    
} 