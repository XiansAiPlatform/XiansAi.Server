using XiansAi.Server.Temporal;
using Temporalio.Client;
using XiansAi.Server.Auth;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Temporalio.Common;
using Shared.Auth;
using Shared.Utils;

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
    public string? AgentName { get; set; }

    [StringLength(100, ErrorMessage = "Assignment name cannot exceed 100 characters")]
    public string? Assignment { get; set; }

    public string? QueueName { get; set; }
}


/// <summary>
/// Handles the creation and initialization of workflows in the Temporal service.
/// </summary>
public class WorkflowStarterService
{

    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowStarterService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowStarterService"/> class.
    /// </summary>
    /// <param name="clientService">The Temporal client service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public WorkflowStarterService(
        ITemporalClientService clientService,
        ILogger<WorkflowStarterService> logger,
        ITenantContext tenantContext)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Handles the HTTP request to start a workflow.
    /// </summary>
    /// <param name="request">The workflow request containing workflow configuration.</param>
    /// <returns>An IResult representing the HTTP response.</returns>
    public async Task<IResult> HandleStartWorkflow(WorkflowRequest request)
    {
        _logger.LogInformation("Received workflow start request of type {WorkflowType} with data {Request}", 
            request?.WorkflowType ?? "null", JsonSerializer.Serialize(request));
        
        if (request == null)
        {
            return Results.BadRequest(new { errors = new[] { "HandleStartWorkflow request body cannot be null" } });
        }
        
        try
        {
            var validationResults = ValidateRequest(request);
            if (validationResults.Count > 0)
            {
                _logger.LogWarning("Invalid workflow request: {Errors}", 
                    string.Join(", ", validationResults));
                return Results.BadRequest(new { errors = validationResults });
            }

            var agentName = request!.AgentName ?? request.WorkflowType;

            var workflowId = NewWorkflowOptions.GenerateNewWorkflowId(request!.WorkflowId, agentName, request.WorkflowType, _tenantContext);
            var options = new NewWorkflowOptions(workflowId, request.WorkflowType, _tenantContext, request.QueueName, agentName, request.Assignment);
            
            _logger.LogDebug("Starting workflow with options: {Options}", JsonSerializer.Serialize(options));
            var handle = await StartWorkflowAsync(request, options);
            
            _logger.LogInformation("Successfully started workflow {WorkflowType} with ID: {WorkflowId}", 
                request.WorkflowType, workflowId);
            
            return Results.Ok(new { 
                message = "Workflow started successfully", 
                workflowId = handle.Id,
                workflowType = request.WorkflowType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting workflow of type {WorkflowType}", request?.WorkflowType);
            return Results.Problem(
                title: "Workflow Start Failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Starts the workflow asynchronously using the Temporal client.
    /// </summary>
    private async Task<WorkflowHandle> StartWorkflowAsync(
        WorkflowRequest request,
        WorkflowOptions options)
    {
        _logger.LogDebug("Starting workflow {WorkflowType} with options {Options}", 
            request.WorkflowType, JsonSerializer.Serialize(options));
        
        var client = _clientService.GetClient();
        return await client.StartWorkflowAsync(
            request.WorkflowType,
            request.Parameters ?? Array.Empty<string>(),
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