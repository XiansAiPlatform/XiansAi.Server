
using XiansAi.Server.MongoDB;
using XiansAi.Server.MongoDB.Models;
using XiansAi.Server.MongoDB.Repositories;

namespace XiansAi.Server.EndpointExt.WebClient;
public class ActivitiesEndpoint
{
    private readonly IDatabaseService _databaseService;

    public ActivitiesEndpoint(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<Activity> GetActivity(string workflowId, string activityId)
    {
        var activityRepository = new ActivityRepository(await _databaseService.GetDatabase());
        return await activityRepository.GetByWorkflowIdAndActivityIdAsync(workflowId, activityId);
    }
}