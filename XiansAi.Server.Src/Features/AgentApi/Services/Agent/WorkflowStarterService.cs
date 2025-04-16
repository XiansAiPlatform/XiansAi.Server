using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Temporalio.Client;
using Temporalio.Common;
using XiansAi.Server.Temporal;
using XiansAi.Server.Auth;
using Shared.Auth;

namespace Features.AgentApi.Services;

/// <summary>
/// Represents a workflow start request.
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
/// Service interface for starting workflows.
/// </summary>
public interface IWorkflowStarterService
{
    Task<IResult> HandleStartWorkflow(WorkflowRequest request);
}

/// <summary>
/// Handles creation and initialization of workflow instances via Temporal.
/// </summary>
public class WorkflowStarterService : IWorkflowStarterService
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowStarterService> _logger;
    private readonly ITenantContext _tenantContext;

    public WorkflowStarterService(
        ITemporalClientService clientService,
        ILogger<WorkflowStarterService> logger,
        ITenantContext tenantContext)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IResult> HandleStartWorkflow(WorkflowRequest request)
    {
        _logger.LogInformation("Received workflow start request of type {WorkflowType} with data {Request}",
            request?.WorkflowType ?? "null", JsonSerializer.Serialize(request));
        Console.WriteLine("-------- Starting workflow ---------");
        // var tenantId = "99xio";
        // var userId = "github|139425589"

        try
        {
            var validationResults = ValidateRequest(request);
            if (validationResults.Count > 0)
            {
                _logger.LogWarning("Invalid workflow request: {Errors}",
                    string.Join(", ", validationResults));
                return Results.BadRequest(new { errors = validationResults });
            }

            Console.WriteLine($"- TenantId: {_tenantContext.TenantId}");
            Console.WriteLine($"- tenant: {_tenantContext}");
            Console.WriteLine($"- LoggedInUser: {_tenantContext.LoggedInUser}");

            // hardcoding tenant id for now
            var workflowId = _tenantContext.TenantId + ":" + (request?.WorkflowId ?? GenerateWorkflowId(request.WorkflowType));
            var options = CreateWorkflowOptions(workflowId, request.WorkflowType, request.AgentName, request.Assignment, request.QueueName);

            _logger.LogDebug("Starting workflow with options: {Options}", JsonSerializer.Serialize(options));
            var handle = await StartWorkflowAsync(request, options);

            _logger.LogInformation("Successfully started workflow {WorkflowType} with ID: {WorkflowId}",
                request.WorkflowType, workflowId);

            return Results.Ok(new
            {
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
    /// Generates a unique workflow ID based on the workflow type and tenant/user context.
    /// </summary>
    private string GenerateWorkflowId(string workflowType)
    {
        if (string.IsNullOrEmpty(_tenantContext.LoggedInUser))
        {
            _logger.LogWarning("No logged-in user found in tenant context");
        }

        return $"{workflowType.Replace(" ", "")}--{_tenantContext.LoggedInUser ?? "-"}--{Guid.NewGuid()}";
    }

    /// <summary>
    /// Creates workflow options including memo data and search attributes.
    /// </summary>
    private WorkflowOptions CreateWorkflowOptions(string workflowId, string workFlowType, string? agent, string? assignment, string? queueName)
    {
        if (string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            throw new InvalidOperationException("TenantId is required to create workflow options");
        }

        if (string.IsNullOrEmpty(_tenantContext.LoggedInUser))
        {
            throw new InvalidOperationException("LoggedInUser is required to create workflow options");
        }

        var memo = new Dictionary<string, object>
        {
            { Constants.TenantIdKey, _tenantContext.TenantId },
            { Constants.AgentKey, agent ?? workFlowType },
            { Constants.UserIdKey, _tenantContext.LoggedInUser! }
        };

        var searchAttributesBuilder = new SearchAttributeCollection.Builder()
            .Set(SearchAttributeKey.CreateKeyword(Constants.TenantIdKey), _tenantContext.TenantId)
            .Set(SearchAttributeKey.CreateKeyword(Constants.AgentKey), agent ?? workFlowType)
            .Set(SearchAttributeKey.CreateKeyword(Constants.UserIdKey), _tenantContext.LoggedInUser!);

        if (!string.IsNullOrEmpty(assignment))
        {
            memo.Add(Constants.AssignmentKey, assignment);
            searchAttributesBuilder.Set(SearchAttributeKey.CreateKeyword(Constants.AssignmentKey), assignment);
        }

        var queueFullName = string.IsNullOrEmpty(queueName) ? workFlowType : queueName + "--" + workFlowType;

        var options = new WorkflowOptions
        {
            TaskQueue = queueFullName,
            Id = workflowId,
            Memo = memo,
            TypedSearchAttributes = searchAttributesBuilder.ToSearchAttributeCollection()
        };

        return options;
    }

    /// <summary>
    /// Uses the Temporal client to start a workflow asynchronously.
    /// </summary>
    private async Task<WorkflowHandle> StartWorkflowAsync(WorkflowRequest request, WorkflowOptions options)
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
    /// Validates the workflow request and returns a list of errors if any.
    /// </summary>
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
