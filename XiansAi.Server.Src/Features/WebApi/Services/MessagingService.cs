using Shared.Auth;
using Shared.Repositories;
using Shared.Utils.Services;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;

namespace Features.WebApi.Services;

public interface IMessagingService
{
    Task<ServiceResult<List<ConversationThread>>> GetThreads(string agent, int? page = null, int? pageSize = null);
    Task<ServiceResult<List<ConversationMessage>>> GetMessages(string threadId, int? page = null, int? pageSize = null);
    Task<ServiceResult<bool>> DeleteThread(string threadId);
}

/// <summary>
/// Endpoint for managing flow definitions with operations for retrieval and deletion.
/// </summary>
public class MessagingService : IMessagingService
{
    private readonly ILogger<MessagingService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IConversationThreadRepository _threadRepository;
    private readonly IConversationMessageRepository _messageRepository;
    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    /// <param name="threadRepository">Repository for thread operations.</param>
    /// <param name="messageRepository">Repository for message operations.</param>
    public MessagingService(
        ILogger<MessagingService> logger,
        ITenantContext tenantContext,
        IConversationThreadRepository threadRepository,
        IConversationMessageRepository messageRepository
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
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
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the thread. Error: " + ex.Message);
        }
    }
}