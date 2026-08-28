using Shared.Utils.Temporal;
using Shared.Utils.Services;
using Shared.Auth;
using Shared.Repositories;
using Shared.Utils;
using Temporalio.Client;

namespace Features.WebApi.Services;

public class WorkflowCancelResult
{
    public string Message { get; set; } = string.Empty;
}

public class CancelAllWorkflowsResult
{
    public int CancelledCount { get; set; }
    public List<string> CancelledWorkflowIds { get; set; } = new();
    public List<string> FailedWorkflowIds { get; set; } = new();
}

public interface IWorkflowCancelService
{
    Task<ServiceResult<WorkflowCancelResult>> CancelWorkflow(string workflowId, bool force);
    Task<ServiceResult<CancelAllWorkflowsResult>> CancelAllWorkflows(bool force);
}

public class WorkflowCancelService : IWorkflowCancelService
{
    private readonly ITemporalGatewayService _temporalGatewayService;
    private readonly ITenantContext _tenantContext;
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<WorkflowCancelService> _logger;

    public WorkflowCancelService(
        ITemporalGatewayService temporalGatewayService,
        ITenantContext tenantContext,
        IAgentRepository agentRepository,
        ILogger<WorkflowCancelService> logger)
    {
        _temporalGatewayService = temporalGatewayService ?? throw new ArgumentNullException(nameof(temporalGatewayService));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _logger = logger;
    }

    /*
    curl -X POST "http://localhost:5000/api/workflows/{workflowId}/cancel?force=true"
    */
    public async Task<ServiceResult<WorkflowCancelResult>> CancelWorkflow(string workflowId, bool force)
    {
        try
        {
            _logger.LogInformation("Cancelling workflow");
            
            if (string.IsNullOrEmpty(workflowId))
            {
                return ServiceResult<WorkflowCancelResult>.BadRequest("WorkflowId is required");
            }

            if (!WorkflowBelongsToCurrentTenant(workflowId))
            {
                _logger.LogWarning(
                    "Rejected cancel for workflow {WorkflowId} that does not belong to tenant {TenantId}",
                    LogSanitizer.Sanitize(workflowId),
                    _tenantContext.TenantId);
                return ServiceResult<WorkflowCancelResult>.Forbidden("Workflow does not belong to this tenant");
            }

            var client = await _temporalGatewayService.GetClientAsync(_tenantContext.TenantId, WorkflowIdentifier.GetAgentName(WorkflowIdentifier.GetWorkflowType(workflowId)));
            var handle = client.GetWorkflowHandle(workflowId);
            
            var result = new WorkflowCancelResult();
            
            if (force)
            {
                await handle.TerminateAsync("Terminated by user request");
                result.Message = $"Workflow {workflowId} termination requested";
            }
            else
            {
                await handle.CancelAsync();
                result.Message = $"Workflow {workflowId} cancellation requested";
            }
            
            return ServiceResult<WorkflowCancelResult>.Success(result);
        } 
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling or terminating workflow");
            return ServiceResult<WorkflowCancelResult>.InternalServerError($"Error canceling or terminating workflow: {ex.Message}");
        }
    }

    public async Task<ServiceResult<CancelAllWorkflowsResult>> CancelAllWorkflows(bool force)
    {
        var loggedInUser = _tenantContext.LoggedInUser;
        if (string.IsNullOrEmpty(loggedInUser))
        {
            return ServiceResult<CancelAllWorkflowsResult>.Unauthorized("User is not authenticated.");
        }

        try
        {
            var tenantId = _tenantContext.TenantId ?? string.Empty;
            var agents = await _agentRepository.GetAgentsWithPermissionAsync(loggedInUser, tenantId);

            if (agents == null || agents.Count == 0)
            {
                _logger.LogInformation("No agents found for tenant {TenantId}, nothing to cancel", tenantId);
                return ServiceResult<CancelAllWorkflowsResult>.Success(new CancelAllWorkflowsResult());
            }

            var agentNames = agents.Select(a => a.Name).ToList();
            var agentList = string.Join(",", agentNames.Select(a => $"'{a}'"));
            var listQuery = $"{Constants.TenantIdKey} = '{tenantId}' and {Constants.AgentKey} in ({agentList}) and ExecutionStatus = 'Running'";

            _logger.LogInformation("Cancelling all running workflows for tenant {TenantId} with query: {Query}", tenantId, listQuery);

            var result = new CancelAllWorkflowsResult();

            var workflowIds = new List<string>();

            await foreach (var client in _temporalGatewayService.GetClientsAsync(tenantId))
            {
                await foreach (var workflow in client.ListWorkflowsAsync(listQuery))
                {
                    if (!string.IsNullOrEmpty(workflow.Id) && workflow.Id.StartsWith(tenantId + ":", StringComparison.Ordinal))
                    {
                        try
                        {
                            var handle = client.GetWorkflowHandle(workflow.Id);

                            if (force)
                            {
                                await handle.TerminateAsync("Terminated by bulk cancel request");
                            }
                            else
                            {
                                await handle.CancelAsync();
                            }

                            result.CancelledWorkflowIds.Add(workflow.Id);
                            result.CancelledCount++;

                            _logger.LogInformation("Successfully {Action} workflow {WorkflowId}", force ? "terminated" : "cancelled", workflow.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to cancel workflow {WorkflowId}", workflow.Id);
                            result.FailedWorkflowIds.Add(workflow.Id);
                        }
                    }
                }
            }

            _logger.LogInformation("{Action} {CancelledCount} workflows for tenant {TenantId}. Failed: {FailedCount}",
                force ? "Terminated" : "Cancelled", result.CancelledCount, tenantId, result.FailedWorkflowIds.Count);

            return ServiceResult<CancelAllWorkflowsResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling all workflows for tenant {TenantId}", _tenantContext.TenantId);
            return ServiceResult<CancelAllWorkflowsResult>.InternalServerError($"Error cancelling all workflows: {ex.Message}");
        }
    }

    private bool WorkflowBelongsToCurrentTenant(string workflowId)
    {
        var tenantId = _tenantContext.TenantId;
        return !string.IsNullOrEmpty(tenantId)
            && workflowId.StartsWith(tenantId + ":", StringComparison.Ordinal);
    }
}
