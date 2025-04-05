
using XiansAi.Server.Database;
using XiansAi.Server.Database.Models;
using XiansAi.Server.Database.Repositories;

namespace Features.WebApi.Services.Web;
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