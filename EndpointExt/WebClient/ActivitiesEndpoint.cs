
using XiansAi.Server.MongoDB;
using XiansAi.Server.MongoDB.Models;
using XiansAi.Server.MongoDB.Repositories;

namespace XiansAi.Server.EndpointExt.WebClient;
public class ActivitiesEndpoint
{
    private readonly ActivityRepository _activityRepository;

    public ActivitiesEndpoint(IMongoDbClientService mongoDbClientService)
    {
        var database = mongoDbClientService.GetDatabase();
        _activityRepository = new ActivityRepository(database);
    }

    public async Task<Activity> GetActivity(string workflowId, string activityId)
    {
        return await _activityRepository.GetByWorkflowIdAndActivityIdAsync(workflowId, activityId);
    }
}