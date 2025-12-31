using Temporalio.Client;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Shared.Auth;
using Shared.Utils.Temporal;
using Shared.Utils.Services;
using Shared.Repositories;
using Features.WebApi.Utils;

namespace Features.WebApi.Services;

/// <summary>
/// Represents a request to start a new workflow.
/// </summary>
public class WorkflowRequest
{
    [Required(ErrorMessage = "WorkflowType is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "WorkflowType must be between 1 and 100 characters")]
    public required string WorkflowType { get; set; }

    [StringLength(200, MinimumLength = 1, ErrorMessage = "WorkflowId must be between 1 and 200 characters")]
    public string? WorkflowId { get; set; }

    public string[]? Parameters { get; set; }

    [StringLength(100, ErrorMessage = "Agent name cannot exceed 100 characters")]
    public required string AgentName { get; set; }

    [StringLength(100, ErrorMessage = "Assignment name cannot exceed 100 characters")]
    public string? Assignment { get; set; }
}

public class WorkflowStartResult
{
    public string Message { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public string WorkflowType { get; set; } = string.Empty;
}

public interface IWorkflowStarterService
{
    Task<ServiceResult<WorkflowStartResult>> HandleStartWorkflow(WorkflowRequest request);
}

/// <summary>
/// Handles the creation and initialization of workflows in the Temporal service.
/// </summary>
public class WorkflowStarterService : IWorkflowStarterService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<WorkflowStarterService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IAgentService _agentService;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowStarterService"/> class.
    /// </summary>
    /// <param name="clientFactory">The Temporal client factory.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="agentService">The agent service.</param>
    /// <param name="flowDefinitionRepository">The flow definition repository.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public WorkflowStarterService(
        ITemporalClientFactory clientFactory,
        ILogger<WorkflowStarterService> logger,
        ITenantContext tenantContext,
        IAgentService agentService,
        IFlowDefinitionRepository flowDefinitionRepository)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
    }

    /// <summary>
    /// Handles the request to start a workflow.
    /// </summary>
    /// <param name="request">The workflow request containing workflow configuration.</param>
    /// <returns>A ServiceResult containing the workflow start result.</returns>
    public async Task<ServiceResult<WorkflowStartResult>> HandleStartWorkflow(WorkflowRequest request)
    {
        _logger.LogInformation("Received workflow start request of type {WorkflowType} with data {Request}", 
            request?.WorkflowType ?? "null", JsonSerializer.Serialize(request));
        
        if (request == null)
        {
            return ServiceResult<WorkflowStartResult>.BadRequest("HandleStartWorkflow request body cannot be null");
        }
        
        try
        {
            var validationResults = ValidateRequest(request);
            if (validationResults.Count > 0)
            {
                _logger.LogWarning("Invalid workflow request: {Errors}", 
                    string.Join(", ", validationResults));
                return ServiceResult<WorkflowStartResult>.BadRequest(string.Join(", ", validationResults));
            }

            var agentName = request.AgentName;
            var systemScoped = _agentService.IsSystemAgent(agentName).Result.Data;

            // Retrieve the flow definition to get parameter type information
            var flowDefinition = await _flowDefinitionRepository.GetLatestFlowDefinitionAsync(
                request.WorkflowType, 
                systemScoped ? null : _tenantContext.TenantId);

            if (flowDefinition == null)
            {
                _logger.LogWarning("Flow definition not found for workflow type: {WorkflowType}", request.WorkflowType);
                return ServiceResult<WorkflowStartResult>.NotFound($"Workflow definition not found for type: {request.WorkflowType}");
            }

            // Convert parameters based on the flow definition
            object[] convertedParameters;
            try
            {
                convertedParameters = request.Parameters != null && request.Parameters.Length > 0
                    ? WorkflowParameterConverter.ConvertParameters(request.Parameters, flowDefinition.ParameterDefinitions)
                    : Array.Empty<object>();
                
                _logger.LogDebug("Converted {Count} parameters for workflow {WorkflowType}", 
                    convertedParameters.Length, request.WorkflowType);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Parameter conversion failed for workflow {WorkflowType}", request.WorkflowType);
                return ServiceResult<WorkflowStartResult>.BadRequest($"Parameter conversion error: {ex.Message}");
            }

            var options = new NewWorkflowOptions(
                agentName, 
                systemScoped,
                request.WorkflowType, 
                request.WorkflowId, 
                _tenantContext);
            
            _logger.LogDebug("Starting workflow with options: {Options}", JsonSerializer.Serialize(options));
            var handle = await StartWorkflowAsync(convertedParameters, options, request.WorkflowType);
            
            var result = new WorkflowStartResult
            {
                Message = "Workflow started successfully",
                WorkflowId = handle.Id,
                WorkflowType = request.WorkflowType
            };
            
            return ServiceResult<WorkflowStartResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting workflow of type {WorkflowType}", request?.WorkflowType);
            return ServiceResult<WorkflowStartResult>.InternalServerError("Workflow start failed");
        }
    }

    /// <summary>
    /// Starts the workflow asynchronously using the Temporal client.
    /// </summary>
    private async Task<WorkflowHandle> StartWorkflowAsync(
        object[] parameters,
        WorkflowOptions options,
        string workflowType)
    {
        _logger.LogDebug("Starting workflow {WorkflowType} with {ParamCount} parameters", 
            workflowType, parameters.Length);
        
        var client = await _clientFactory.GetClientAsync();
        return await client.StartWorkflowAsync(
            workflowType,
            parameters,
            options
        );
    }

    /// <summary>
    /// Validates the workflow request.
    /// </summary>
    /// <returns>A list of validation errors, empty if validation succeeds.</returns>
    private static List<string> ValidateRequest(WorkflowRequest? request)
    {
        var errors = new List<string>();

        if (request == null)
        {
            errors.Add("Request body cannot be null");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.WorkflowType))
        {
            errors.Add("WorkflowType is required and cannot be empty");
        }
        else if (request.WorkflowType.Length > 100)
        {
            errors.Add("WorkflowType cannot exceed 100 characters");
        }

        if (request.AgentName?.Length > 100)
        {
            errors.Add("Agent name cannot exceed 100 characters");
        }

        if (request.Assignment?.Length > 100)
        {
            errors.Add("Assignment name cannot exceed 100 characters");
        }

        if (request.Parameters != null)
        {
            if (request.Parameters.Any(p => p == null))
            {
                errors.Add("Workflow parameters cannot contain null values");
            }
        }

        return errors;
    }
}