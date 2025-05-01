using Shared.Auth;
using Shared.Repositories;
using Shared.Utils.Services;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;

namespace Features.WebApi.Services;

public interface IMessagingService
{
    Task<IResult> GetGroupedDefinitions();
    Task<IResult> GetWorkflowInstances(string? agentName, string? typeName);
    Task<ServiceResult<List<ConversationThread>>> GetThreads(string agent, int? page = null, int? pageSize = null);
    Task<ServiceResult<List<ConversationMessage>>> GetMessages(string threadId, int? page = null, int? pageSize = null);
    Task<ServiceResult<bool>> DeleteThread(string threadId);
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

    private readonly IConversationThreadRepository _threadRepository;
    private readonly IConversationMessageRepository _messageRepository;
    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionsService"/> class.
    /// </summary>
    /// <param name="definitionRepository">Repository for flow definition operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    /// <param name="workflowFinderService">Service for workflow finder operations.</param>
    /// <param name="threadRepository">Repository for thread operations.</param>
    /// <param name="messageRepository">Repository for message operations.</param>
    public MessagingService(
        IFlowDefinitionRepository definitionRepository,
        ILogger<MessagingService> logger,
        ITenantContext tenantContext,
        IWorkflowFinderService workflowFinderService,
        IConversationThreadRepository threadRepository,
        IConversationMessageRepository messageRepository
    )
    {
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _workflowFinderService = workflowFinderService ?? throw new ArgumentNullException(nameof(workflowFinderService));
        _threadRepository = threadRepository ?? throw new ArgumentNullException(nameof(threadRepository));
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
    }
    

    public async Task<ServiceResult<List<ConversationMessage>>> GetMessages(string threadId, int? page = null, int? pageSize = null)
    {
        var tenantId = _tenantContext.TenantId;
        
        // Validate pagination parameters
        if (page.HasValue && page.Value <= 0)
        {
            page = 1;
        }
        
        if (pageSize.HasValue && pageSize.Value <= 0)
        {
            pageSize = 10;
        }
        
        var messages = await _messageRepository.GetByThreadIdAsync(tenantId, threadId, page, pageSize);
        return ServiceResult<List<ConversationMessage>>.Success(messages);
    }

    public async Task<ServiceResult<List<ConversationThread>>> GetThreads(string agent, int? page = null, int? pageSize = null)
    {
        var tenantId = _tenantContext.TenantId;
        
        // Validate pagination parameters
        if (page.HasValue && page.Value <= 0)
        {
            page = 1;
        }
        
        if (pageSize.HasValue && pageSize.Value <= 0)
        {
            pageSize = 10;
        }
        
        var threads = await _threadRepository.GetByTenantAndAgentAsync(tenantId, agent, page, pageSize);
        return ServiceResult<List<ConversationThread>>.Success(threads);
    }

    public async Task<IResult> GetGroupedDefinitions()
    {
        try
        {
            var definitions = await _definitionRepository.GetDefinitionsWithPermissionAsync(_tenantContext.LoggedInUser, null, null, basicDataOnly: true);
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

    public async Task<ServiceResult<bool>> DeleteThread(string threadId)
    {
        try
        {
            var result = await _threadRepository.DeleteAsync(threadId);
            if (!result)
            {
                return ServiceResult<bool>.BadRequest("Thread not found or could not be deleted");
            }
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting thread {ThreadId}", threadId);
            return ServiceResult<bool>.BadRequest("An error occurred while deleting the thread");
        }
    }
}