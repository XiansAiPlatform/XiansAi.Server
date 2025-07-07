using MongoDB.Driver;
using MongoDB.Bson;
using Shared.Data;
using Features.WebApi.Models;

namespace Features.WebApi.Repositories;

public interface IActivityRepository
{
    Task<Activity> GetByWorkflowIdAndActivityIdAsync(string workflowId, string activityId);
    Task<List<Activity>> GetByWorkflowIdAsync(string workflowId);
    Task<List<Activity>> GetByWorkflowTypeAsync(string workflowType);
    Task<List<Activity>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task CreateAsync(Activity activity);
    Task<bool> UpdateAsync(string activityId, Activity activity);
    Task<bool> UpdateEndTimeAsync(string activityId, DateTime endTime, object result);
    Task<List<Activity>> GetActiveActivitiesAsync();
    Task<List<Activity>> GetByTaskQueueAsync(string taskQueue);
    Task<bool> DeleteAsync(string activityId);
    Task<List<Activity>> SearchAsync(string searchTerm);
}

public class ActivityRepository : IActivityRepository
{
    private readonly IMongoCollection<Activity> _activities;

    public ActivityRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _activities = database.GetCollection<Activity>("activity_history");
    }

    public async Task<Activity> GetByWorkflowIdAndActivityIdAsync(string workflowId, string activityId)
    {
        return await _activities.Find(x => x.WorkflowId == workflowId && x.ActivityId == activityId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Activity>> GetByWorkflowIdAsync(string workflowId)
    {
        return await _activities.Find(x => x.WorkflowId == workflowId)
            .SortByDescending(x => x.StartedTime)
            .ToListAsync();
    }

    public async Task<List<Activity>> GetByWorkflowTypeAsync(string workflowType)
    {
        return await _activities.Find(x => x.WorkflowType == workflowType)
            .SortByDescending(x => x.StartedTime)
            .ToListAsync();
    }

    public async Task<List<Activity>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _activities.Find(x => x.StartedTime >= startDate && x.StartedTime <= endDate)
            .SortByDescending(x => x.StartedTime)
            .ToListAsync();
    }

    public async Task CreateAsync(Activity activity)
    {
        await _activities.InsertOneAsync(activity);
    }

    public async Task<bool> UpdateAsync(string activityId, Activity activity)
    {
        var result = await _activities.ReplaceOneAsync(x => x.ActivityId == activityId, activity);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateEndTimeAsync(string activityId, DateTime endTime, object result)
    {
        var update = Builders<Activity>.Update
            .Set(x => x.EndedTime, endTime)
            .Set(x => x.Result, result);
            
        var returnResult = await _activities.UpdateOneAsync(x => x.ActivityId == activityId, update);
        return returnResult.ModifiedCount > 0;
    }

    public async Task<List<Activity>> GetActiveActivitiesAsync()
    {
        return await _activities.Find(x => x.EndedTime == null)
            .SortByDescending(x => x.StartedTime)
            .ToListAsync();
    }

    public async Task<List<Activity>> GetByTaskQueueAsync(string taskQueue)
    {
        return await _activities.Find(x => x.TaskQueue == taskQueue)
            .SortByDescending(x => x.StartedTime)
            .ToListAsync();
    }

    public async Task<bool> DeleteAsync(string activityId)
    {
        var result = await _activities.DeleteOneAsync(x => x.ActivityId == activityId);
        return result.DeletedCount > 0;
    }

    public async Task<List<Activity>> SearchAsync(string searchTerm)
    {
        var filter = Builders<Activity>.Filter.Or(
            Builders<Activity>.Filter.Regex(x => x.ActivityName, new BsonRegularExpression(searchTerm, "i")),
            Builders<Activity>.Filter.Regex(x => x.WorkflowType, new BsonRegularExpression(searchTerm, "i"))
        );

        return await _activities.Find(filter)
            .SortByDescending(x => x.StartedTime)
            .ToListAsync();
    }
}
