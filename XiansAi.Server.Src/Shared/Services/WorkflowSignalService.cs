using Shared.Auth;
using Shared.Utils;
using System.Text.Json.Serialization;
using Shared.Utils.Temporal;

namespace Shared.Services;

public class WorkflowEventRequest
{
    public object? Payload { get; set; }
    public required string WorkflowId { get; set; }

    public WorkflowSignalRequest ToWorkflowSignalRequest()
    {
       return new WorkflowSignalRequest
       {
            SignalName = Constants.SIGNAL_INBOUND_EVENT,
            WorkflowId = WorkflowId,
            Payload = Payload
       };
    }
}

public class WorkflowSignalRequest
{
    public required string SignalName { get; set; }
    public object? Payload { get; set; }
    public required string WorkflowId { get; set; }
}

public class WorkflowSignalWithStartRequest
{
    [JsonPropertyName("SignalName")]
    public string? SignalName { get; set; }
    [JsonPropertyName("SourceAgent")]
    public required string SourceAgent { get; set; }

    [JsonPropertyName("TargetWorkflowId")]
    public string? TargetWorkflowId { get; set; }

    [JsonPropertyName("TargetWorkflowType")]
    public required string TargetWorkflowType { get; set; }

    [JsonPropertyName("Payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("SourceWorkflowId")]
    public string? SourceWorkflowId { get; set; }

    [JsonPropertyName("SourceWorkflowType")]
    public string? SourceWorkflowType { get; set; }



}

/// <summary>
/// Handles API endpoints for signaling Temporal workflows.
/// </summary>
public interface IWorkflowSignalService
{
    Task<IResult> SignalWithStartWorkflow(WorkflowSignalWithStartRequest request);
}

public class WorkflowSignalService : IWorkflowSignalService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<WorkflowSignalService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowSignalService"/> class.
    /// </summary>
    /// <param name="clientFactory">The factory for obtaining Temporal clients.</param>
    /// <param name="logger">The logger for recording operational information.</param>
    /// <param name="tenantContext">The tenant context for the current request.</param>
    /// <exception cref="ArgumentNullException">Thrown when any of the required services is null.</exception>
    public WorkflowSignalService(
        ITemporalClientFactory clientFactory,
        ILogger<WorkflowSignalService> logger,
        ITenantContext tenantContext)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IResult> SignalWithStartWorkflow(WorkflowSignalWithStartRequest request)
    {
        try
        {
            var client = await _clientFactory.GetClientAsync() ?? throw new Exception("Failed to get Temporal client");

            var options = new NewWorkflowOptions(
                request.SourceAgent, 
                request.TargetWorkflowType, 
                request.TargetWorkflowId, 
                _tenantContext);
       
            var signalPayload = new object[] { request };

            if (request.SignalName == null) throw new Exception("SignalName is required");

            options.SignalWithStart(request.SignalName, signalPayload);

            _logger.LogInformation("Starting workflow {WorkflowType} with signal {SignalName}", 
                request.TargetWorkflowType, request.SignalName);

            await client.StartWorkflowAsync(request.TargetWorkflowType, new List<object>().AsReadOnly(), options);

            _logger.LogInformation("Successfully started workflow {WorkflowType} with signal {SignalName}", 
                request.TargetWorkflowType, request.SignalName);
            
            return Results.Ok(new { 
                message = "Signal with start sent successfully", 
                workflowId = request.TargetWorkflowId,
                signalName = request.SignalName
            });
        }
        catch (Temporalio.Exceptions.RpcException ex) when (ex.Message.Contains("workflow not found"))
        {
            _logger.LogWarning(ex, "Workflow reference not found for type: {WorkflowType}", request.TargetWorkflowType);
            return Results.NotFound(new {
                message = $"Workflow type '{request.TargetWorkflowType}' could not be started or referenced",
                workflowType = request.TargetWorkflowType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending signal {SignalName} to workflow {WorkflowType}", 
                request.SignalName, request.TargetWorkflowType);
                
            throw;
        }
    }
}