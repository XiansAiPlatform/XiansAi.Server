using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;

namespace Features.WebApi.Services;
public class ActivitiesService
{
    private readonly IActivityRepository _activityRepository;

    public ActivitiesService(IActivityRepository activityRepository)
    {
        _activityRepository = activityRepository ?? throw new ArgumentNullException(nameof(activityRepository));
    }

    public async Task<Activity> GetActivity(string workflowId, string activityId)
    {
        return await _activityRepository.GetByWorkflowIdAndActivityIdAsync(workflowId, activityId);
    }
}