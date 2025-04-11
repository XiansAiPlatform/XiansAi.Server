using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;

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