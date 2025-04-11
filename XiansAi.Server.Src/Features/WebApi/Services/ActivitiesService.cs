
using XiansAi.Server.Database;
using XiansAi.Server.Database.Models;
using XiansAi.Server.Database.Repositories;

namespace Features.WebApi.Services;
public class ActivitiesService
{
    private readonly IDatabaseService _databaseService;

    public ActivitiesService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<Activity> GetActivity(string workflowId, string activityId)
    {
        var activityRepository = new ActivityRepository(await _databaseService.GetDatabase());
        return await activityRepository.GetByWorkflowIdAndActivityIdAsync(workflowId, activityId);
    }
}