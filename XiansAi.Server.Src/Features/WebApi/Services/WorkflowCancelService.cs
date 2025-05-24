using Shared.Utils.Temporal;
using Shared.Utils.Services;

namespace Features.WebApi.Services;

public class WorkflowCancelResult
{
    public string Message { get; set; } = string.Empty;
}

public interface IWorkflowCancelService
{
    Task<ServiceResult<WorkflowCancelResult>> CancelWorkflow(string workflowId, bool force);
}

public class WorkflowCancelService : IWorkflowCancelService
{
    private readonly ITemporalClientService _temporalClientService;
    private readonly ILogger<WorkflowCancelService> _logger;

    public WorkflowCancelService(ITemporalClientService temporalClientService, ILogger<WorkflowCancelService> logger)
    {
        _temporalClientService = temporalClientService;
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

            var client = _temporalClientService.GetClient();
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
            return ServiceResult<WorkflowCancelResult>.BadRequest($"Error canceling or terminating workflow: {ex.Message}");
        }
    }
}
