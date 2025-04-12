using Shared.Auth;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;

namespace Features.WebApi.Services;

public interface IMessagingService
{
    Task<IResult> GetGroupedDefinitions();
    Task<IResult> GetWorkflowInstances(string? agentName, string? typeName);
}

/// <summary>
/// Endpoint for managing flow definitions with operations for retrieval and deletion.
/// </summary>
public class MessagingService : IMessagingService
{
    private readonly IFlowDefinitionRepository _definitionRepository;
    private readonly ILogger<MessagingService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IWorkflowFinderService _workflowFinderService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionsService"/> class.
    /// </summary>
    /// <param name="definitionRepository">Repository for flow definition operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    /// <param name="workflowFinderService">Service for workflow finder operations.</param>
    public MessagingService(
        IFlowDefinitionRepository definitionRepository,
        ILogger<MessagingService> logger,
        ITenantContext tenantContext,
        IWorkflowFinderService workflowFinderService
    )
    {
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _workflowFinderService = workflowFinderService ?? throw new ArgumentNullException(nameof(workflowFinderService));
    }
    
    public async Task<IResult> GetGroupedDefinitions()
    {
        try
        {
            var definitions = await _definitionRepository.GetLatestDefinitionsBasicDataAsync();
            return Results.Ok(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving definitions");
            return Results.Problem("An error occurred while retrieving definitions.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> GetWorkflowInstances(string? agentName, string? typeName)
    {
        try
        {
            var workflows = await _workflowFinderService.GetRunningWorkflowsByAgentAndType(agentName, typeName);
            return Results.Ok(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflows");
            return Results.Problem("An error occurred while retrieving workflows.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}