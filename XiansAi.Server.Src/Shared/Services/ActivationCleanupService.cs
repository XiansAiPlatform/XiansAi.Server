using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Temporalio.Client;
using Features.WebApi.Services;
using Shared.Data.Models;

namespace Shared.Services;

/// <summary>
/// Service interface for cleaning up workflows and schedules associated with an activation.
/// </summary>
public interface IActivationCleanupService
{
    /// <summary>
    /// Finds all workflows associated with an activation.
    /// </summary>
    /// <param name="tenantId">The tenant ID to filter workflows</param>
    /// <param name="agentName">The agent name to filter workflows</param>
    /// <param name="idPostfix">The ID postfix (activation name) to filter workflows</param>
    /// <returns>List of workflow IDs</returns>
    Task<ServiceResult<List<string>>> FindWorkflowsByActivationAsync(
        string tenantId, 
        string agentName, 
        string idPostfix);

    /// <summary>
    /// Finds all schedules associated with an activation.
    /// </summary>
    /// <param name="tenantId">The tenant ID to filter schedules</param>
    /// <param name="agentName">The agent name to filter schedules</param>
    /// <param name="idPostfix">The ID postfix (activation name) to filter schedules</param>
    /// <returns>List of schedule IDs</returns>
    Task<ServiceResult<List<string>>> FindSchedulesByActivationAsync(
        string tenantId, 
        string agentName, 
        string idPostfix);

    /// <summary>
    /// Cancels/terminates all running workflows by their IDs.
    /// </summary>
    /// <param name="workflowIds">List of workflow IDs to cancel</param>
    /// <param name="tenantId">The tenant ID for getting the Temporal client</param>
    /// <returns>Result with count of cancelled workflows</returns>
    Task<ServiceResult<WorkflowCleanupResult>> CancelWorkflowsAsync(
        List<string> workflowIds, 
        string tenantId);

    /// <summary>
    /// Deletes all schedules by their IDs.
    /// </summary>
    /// <param name="scheduleIds">List of schedule IDs to delete</param>
    /// <param name="tenantId">The tenant ID for getting the Temporal client</param>
    /// <returns>Result with count of deleted schedules</returns>
    Task<ServiceResult<ScheduleCleanupResult>> DeleteSchedulesAsync(
        List<string> scheduleIds, 
        string tenantId);

    /// <summary>
    /// Performs complete cleanup of workflows and schedules for an activation.
    /// </summary>
    /// <param name="activation">The activation to clean up</param>
    /// <returns>Result with cleanup statistics</returns>
    Task<ServiceResult<ActivationCleanupResult>> CleanupActivationResourcesAsync(
        AgentActivation activation);
}

/// <summary>
/// Result model for workflow cleanup operations
/// </summary>
public class WorkflowCleanupResult
{
    public int TotalWorkflows { get; set; }
    public int CancelledCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> CancelledWorkflowIds { get; set; } = new();
    public List<string> FailedWorkflowIds { get; set; } = new();
}

/// <summary>
/// Result model for schedule cleanup operations
/// </summary>
public class ScheduleCleanupResult
{
    public int TotalSchedules { get; set; }
    public int DeletedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> DeletedScheduleIds { get; set; } = new();
    public List<string> FailedScheduleIds { get; set; } = new();
}

/// <summary>
/// Result model for complete activation cleanup
/// </summary>
public class ActivationCleanupResult
{
    public WorkflowCleanupResult WorkflowCleanup { get; set; } = new();
    public ScheduleCleanupResult ScheduleCleanup { get; set; } = new();
    public bool Success => 
        WorkflowCleanup.FailedCount == 0 && 
        ScheduleCleanup.FailedCount == 0;
}

/// <summary>
/// Service for cleaning up workflows and schedules associated with an activation.
/// </summary>
public class ActivationCleanupService : IActivationCleanupService
{
    private readonly ITemporalClientService _temporalClientService;
    private readonly ILogger<ActivationCleanupService> _logger;

    public ActivationCleanupService(
        ITemporalClientService temporalClientService,
        ILogger<ActivationCleanupService> logger)
    {
        _temporalClientService = temporalClientService ?? throw new ArgumentNullException(nameof(temporalClientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Finds all workflows associated with an activation by querying Temporal with search attributes.
    /// </summary>
    public async Task<ServiceResult<List<string>>> FindWorkflowsByActivationAsync(
        string tenantId, 
        string agentName, 
        string idPostfix)
    {
        try
        {
            _logger.LogInformation(
                "Searching for workflows with tenantId={TenantId}, agent={Agent}, idPostfix={IdPostfix}",
                tenantId, agentName, idPostfix);

            var client = await _temporalClientService.GetClientAsync(tenantId);
            var workflowIds = new List<string>();

            // Build query using search attributes
            var queryParts = new List<string>
            {
                $"tenantId = '{tenantId}'",
                $"agent = '{agentName}'",
                $"idPostfix = '{idPostfix}'"
            };

            var query = string.Join(" AND ", queryParts);
            _logger.LogDebug("Executing workflow query: {Query}", query);

            // Query all workflows (including completed ones) that match the criteria
            await foreach (var workflow in client.ListWorkflowsAsync(query))
            {
                if (!string.IsNullOrEmpty(workflow.Id))
                {
                    workflowIds.Add(workflow.Id);
                    _logger.LogDebug("Found workflow: {WorkflowId} with status {Status}", 
                        workflow.Id, workflow.Status);
                }
            }

            _logger.LogInformation("Found {Count} workflows for activation", workflowIds.Count);
            return ServiceResult<List<string>>.Success(workflowIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding workflows for activation");
            return ServiceResult<List<string>>.InternalServerError(
                $"Failed to find workflows: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds all schedules associated with an activation by querying Temporal schedules.
    /// </summary>
    public async Task<ServiceResult<List<string>>> FindSchedulesByActivationAsync(
        string tenantId, 
        string agentName, 
        string idPostfix)
    {
        try
        {
            _logger.LogInformation(
                "Searching for schedules with tenantId={TenantId}, agent={Agent}, idPostfix={IdPostfix}",
                tenantId, agentName, idPostfix);

            var client = await _temporalClientService.GetClientAsync(tenantId);
            var scheduleIds = new List<string>();

            // Build query for schedules using search attributes
            var queryParts = new List<string>
            {
                $"tenantId = '{tenantId}'",
                $"agent = '{agentName}'",
                $"idPostfix = '{idPostfix}'"
            };

            var query = string.Join(" AND ", queryParts);
            _logger.LogDebug("Executing schedule query: {Query}", query);

            var listOptions = new Temporalio.Client.Schedules.ScheduleListOptions 
            { 
                Query = query 
            };

            await foreach (var schedule in client.ListSchedulesAsync(listOptions))
            {
                if (!string.IsNullOrEmpty(schedule.Id))
                {
                    scheduleIds.Add(schedule.Id);
                    _logger.LogDebug("Found schedule: {ScheduleId}", schedule.Id);
                }
            }

            _logger.LogInformation("Found {Count} schedules for activation", scheduleIds.Count);
            return ServiceResult<List<string>>.Success(scheduleIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding schedules for activation");
            return ServiceResult<List<string>>.InternalServerError(
                $"Failed to find schedules: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancels all running workflows by their IDs.
    /// </summary>
    public async Task<ServiceResult<WorkflowCleanupResult>> CancelWorkflowsAsync(
        List<string> workflowIds, 
        string tenantId)
    {
        var result = new WorkflowCleanupResult
        {
            TotalWorkflows = workflowIds.Count
        };

        if (workflowIds.Count == 0)
        {
            _logger.LogInformation("No workflows to cancel");
            return ServiceResult<WorkflowCleanupResult>.Success(result);
        }

        try
        {
            var client = await _temporalClientService.GetClientAsync(tenantId);

            foreach (var workflowId in workflowIds)
            {
                try
                {
                    var handle = client.GetWorkflowHandle(workflowId);
                    
                    // Check if workflow is still running before attempting to cancel
                    try
                    {
                        var description = await handle.DescribeAsync();
                        var status = description.Status;

                        // Only try to cancel if the workflow is in a running state
                        if (status == Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Running ||
                            status == Temporalio.Api.Enums.V1.WorkflowExecutionStatus.ContinuedAsNew)
                        {
                            await handle.CancelAsync();
                            result.CancelledWorkflowIds.Add(workflowId);
                            result.CancelledCount++;
                            _logger.LogInformation("Cancelled workflow {WorkflowId}", workflowId);
                        }
                        else
                        {
                            _logger.LogDebug("Workflow {WorkflowId} is not running (status: {Status}), skipping cancellation", 
                                workflowId, status);
                            // Don't count as failed since it's already stopped
                            result.CancelledWorkflowIds.Add(workflowId);
                            result.CancelledCount++;
                        }
                    }
                    catch (Exception describeEx)
                    {
                        _logger.LogWarning(describeEx, 
                            "Failed to describe workflow {WorkflowId}, attempting cancellation anyway", 
                            workflowId);
                        
                        // Try to cancel anyway
                        await handle.CancelAsync();
                        result.CancelledWorkflowIds.Add(workflowId);
                        result.CancelledCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cancel workflow {WorkflowId}", workflowId);
                    result.FailedWorkflowIds.Add(workflowId);
                    result.FailedCount++;
                }
            }

            _logger.LogInformation(
                "Workflow cancellation complete: {Cancelled}/{Total} cancelled, {Failed} failed",
                result.CancelledCount, result.TotalWorkflows, result.FailedCount);

            return ServiceResult<WorkflowCleanupResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling workflows");
            return ServiceResult<WorkflowCleanupResult>.InternalServerError(
                $"Failed to cancel workflows: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes all schedules by their IDs.
    /// </summary>
    public async Task<ServiceResult<ScheduleCleanupResult>> DeleteSchedulesAsync(
        List<string> scheduleIds, 
        string tenantId)
    {
        var result = new ScheduleCleanupResult
        {
            TotalSchedules = scheduleIds.Count
        };

        if (scheduleIds.Count == 0)
        {
            _logger.LogInformation("No schedules to delete");
            return ServiceResult<ScheduleCleanupResult>.Success(result);
        }

        try
        {
            var client = await _temporalClientService.GetClientAsync(tenantId);

            foreach (var scheduleId in scheduleIds)
            {
                try
                {
                    var scheduleHandle = client.GetScheduleHandle(scheduleId);
                    await scheduleHandle.DeleteAsync();
                    
                    result.DeletedScheduleIds.Add(scheduleId);
                    result.DeletedCount++;
                    _logger.LogInformation("Deleted schedule {ScheduleId}", scheduleId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete schedule {ScheduleId}", scheduleId);
                    result.FailedScheduleIds.Add(scheduleId);
                    result.FailedCount++;
                }
            }

            _logger.LogInformation(
                "Schedule deletion complete: {Deleted}/{Total} deleted, {Failed} failed",
                result.DeletedCount, result.TotalSchedules, result.FailedCount);

            return ServiceResult<ScheduleCleanupResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting schedules");
            return ServiceResult<ScheduleCleanupResult>.InternalServerError(
                $"Failed to delete schedules: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs complete cleanup of workflows and schedules for an activation.
    /// This is the main method that orchestrates the entire cleanup process.
    /// </summary>
    public async Task<ServiceResult<ActivationCleanupResult>> CleanupActivationResourcesAsync(
        AgentActivation activation)
    {
        var cleanupResult = new ActivationCleanupResult();

        try
        {
            _logger.LogInformation(
                "Starting cleanup for activation {ActivationId} (Name: {Name}, Agent: {Agent}, Tenant: {Tenant})",
                activation.Id, activation.Name, activation.AgentName, activation.TenantId);

            // Step 1: Find all workflows
            var workflowsResult = await FindWorkflowsByActivationAsync(
                activation.TenantId,
                activation.AgentName,
                activation.Name);

            if (!workflowsResult.IsSuccess)
            {
                _logger.LogWarning("Failed to find workflows: {Error}", workflowsResult.ErrorMessage);
                return ServiceResult<ActivationCleanupResult>.InternalServerError(
                    $"Failed to find workflows: {workflowsResult.ErrorMessage}");
            }

            // Step 2: Find all schedules
            var schedulesResult = await FindSchedulesByActivationAsync(
                activation.TenantId,
                activation.AgentName,
                activation.Name);

            if (!schedulesResult.IsSuccess)
            {
                _logger.LogWarning("Failed to find schedules: {Error}", schedulesResult.ErrorMessage);
                return ServiceResult<ActivationCleanupResult>.InternalServerError(
                    $"Failed to find schedules: {schedulesResult.ErrorMessage}");
            }

            _logger.LogInformation(
                "Found {WorkflowCount} workflows and {ScheduleCount} schedules to clean up",
                workflowsResult.Data!.Count, schedulesResult.Data!.Count);

            // Step 3: Cancel all workflows
            var cancelResult = await CancelWorkflowsAsync(
                workflowsResult.Data,
                activation.TenantId);

            if (cancelResult.IsSuccess && cancelResult.Data != null)
            {
                cleanupResult.WorkflowCleanup = cancelResult.Data;
            }
            else
            {
                _logger.LogWarning("Failed to cancel workflows: {Error}", cancelResult.ErrorMessage);
                return ServiceResult<ActivationCleanupResult>.InternalServerError(
                    $"Failed to cancel workflows: {cancelResult.ErrorMessage}");
            }

            // Step 4: Delete all schedules
            var deleteResult = await DeleteSchedulesAsync(
                schedulesResult.Data,
                activation.TenantId);

            if (deleteResult.IsSuccess && deleteResult.Data != null)
            {
                cleanupResult.ScheduleCleanup = deleteResult.Data;
            }
            else
            {
                _logger.LogWarning("Failed to delete schedules: {Error}", deleteResult.ErrorMessage);
                return ServiceResult<ActivationCleanupResult>.InternalServerError(
                    $"Failed to delete schedules: {deleteResult.ErrorMessage}");
            }

            _logger.LogInformation(
                "Cleanup complete for activation {ActivationId}: " +
                "Workflows: {CancelledWorkflows}/{TotalWorkflows} cancelled, " +
                "Schedules: {DeletedSchedules}/{TotalSchedules} deleted",
                activation.Id,
                cleanupResult.WorkflowCleanup.CancelledCount,
                cleanupResult.WorkflowCleanup.TotalWorkflows,
                cleanupResult.ScheduleCleanup.DeletedCount,
                cleanupResult.ScheduleCleanup.TotalSchedules);

            return ServiceResult<ActivationCleanupResult>.Success(cleanupResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during activation cleanup for {ActivationId}", activation.Id);
            return ServiceResult<ActivationCleanupResult>.InternalServerError(
                $"Failed to cleanup activation resources: {ex.Message}");
        }
    }
}
