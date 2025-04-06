using MongoDB.Driver;
using Features.AgentApi.Data.Models;
using XiansAi.Server.Utils;

namespace Features.AgentApi.Data.Repositories;

public class ActivityHistoryRepository
{
    private readonly IMongoCollection<ActivityHistory> _activities;
    private readonly IBackgroundTaskService _backgroundTaskService;
    private readonly ILogger<ActivityHistoryRepository> _logger;

    public ActivityHistoryRepository(
        IMongoDatabase database, 
        IBackgroundTaskService backgroundTaskService,
        ILogger<ActivityHistoryRepository> logger)
    {
        _activities = database.GetCollection<ActivityHistory>("activity-history");
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