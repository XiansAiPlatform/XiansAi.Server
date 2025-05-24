using Features.WebApi.Models;
using Features.WebApi.Repositories;
using Shared.Utils.Services;

namespace Features.WebApi.Services;

public interface IActivitiesService
{
    Task<ServiceResult<Activity>> GetActivity(string workflowId, string activityId);
}

public class ActivitiesService : IActivitiesService
{
    private readonly IActivityRepository _activityRepository;
    private readonly ILogger<ActivitiesService> _logger;

    public ActivitiesService(IActivityRepository activityRepository, ILogger<ActivitiesService> logger)
    {
        _activityRepository = activityRepository ?? throw new ArgumentNullException(nameof(activityRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<Activity>> GetActivity(string workflowId, string activityId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workflowId))
            {
                _logger.LogWarning("Invalid workflow ID provided for getting activity");
                return ServiceResult<Activity>.BadRequest("Workflow ID is required");
            }

            if (string.IsNullOrWhiteSpace(activityId))
            {
                _logger.LogWarning("Invalid activity ID provided for getting activity");
                return ServiceResult<Activity>.BadRequest("Activity ID is required");
            }

            var activity = await _activityRepository.GetByWorkflowIdAndActivityIdAsync(workflowId, activityId);
            if (activity == null)
            {
                _logger.LogWarning("Activity {ActivityId} not found for workflow {WorkflowId}", activityId, workflowId);
                return ServiceResult<Activity>.NotFound("Activity not found");
            }

            return ServiceResult<Activity>.Success(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving activity {ActivityId} for workflow {WorkflowId}", activityId, workflowId);
            return ServiceResult<Activity>.InternalServerError("An error occurred while retrieving the activity. Error: " + ex.Message);
        }
    }
}