using Features.WebApi.Models;
using Features.WebApi.Repositories;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

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
          var   validatedActivityId = Activity.SanitizeAndValidateActivityId(activityId);
           var validatedWorkflowId = Activity.SanitizeAndValidateWorkflowId(workflowId);
            
            var activity = await _activityRepository.GetByWorkflowIdAndActivityIdAsync(validatedWorkflowId, validatedActivityId);
            if (activity == null)
            {
                _logger.LogWarning("Activity {ActivityId} not found for workflow {WorkflowId}", validatedActivityId, validatedWorkflowId);
                return ServiceResult<Activity>.NotFound("Activity not found");
            }

            return ServiceResult<Activity>.Success(activity);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving activity: {Message}", ex.Message);
            return ServiceResult<Activity>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving activity {ActivityId} for workflow {WorkflowId}", activityId, workflowId);
            return ServiceResult<Activity>.InternalServerError("An error occurred while retrieving the activity. Error: " + ex.Message);
        }
    }
}