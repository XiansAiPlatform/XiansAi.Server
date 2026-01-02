using Shared.Auth;
using Shared.Repositories;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

namespace Features.WebApi.Services;

public interface IMessagingService
{
    Task<ServiceResult<List<ConversationThread>>> GetThreads(string agent, int? page = null, int? pageSize = null);
    Task<ServiceResult<List<ConversationMessage>>> GetMessages(string threadId, int? page = null, int? pageSize = null, string? scope = null);
    Task<ServiceResult<TopicsResult>> GetTopics(string threadId, int page, int pageSize);
    Task<ServiceResult<bool>> DeleteThread(string threadId);
}

/// <summary>
/// Endpoint for managing flow definitions with operations for retrieval and deletion.
/// </summary>
public class MessagingService : IMessagingService
{
    private readonly ILogger<MessagingService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IConversationRepository _conversationRepository;
    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    /// <param name="conversationRepository">Repository for conversation operations.</param>
    public MessagingService(
        ILogger<MessagingService> logger,
        ITenantContext tenantContext,
        IConversationRepository conversationRepository
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
    }


    public async Task<ServiceResult<List<ConversationMessage>>> GetMessages(string threadId, int? page = null, int? pageSize = null, string? scope = null)
    {
        var tenantId = _tenantContext.TenantId;

        // Validate pagination parameters
        if (page.HasValue && page.Value <= 0)
        {
            return ServiceResult<List<ConversationMessage>>.BadRequest("Page number must be greater than 0. Pagination is 1-based.");
        }

        if (pageSize.HasValue && pageSize.Value <= 0)
        {
            return ServiceResult<List<ConversationMessage>>.BadRequest("Page size must be greater than 0.");
        }

        // Ensure both page and pageSize are set if either is provided
        if (page.HasValue || pageSize.HasValue)
        {
            if (!page.HasValue)
            {
                page = 1;
            }

            if (!pageSize.HasValue)
            {
                pageSize = 10;
            }
        }

        // Handle scope parameter:
        // - null: no filtering (return all messages)
        // - empty string: filter for messages with null scope
        // - value: filter for messages with that scope
        // Trim whitespace from non-empty scopes
        string? normalizedScope = scope;
        if (!string.IsNullOrEmpty(scope))
        {
            normalizedScope = scope.Trim();
            // If after trimming it's empty, treat as empty string (filter for null scopes)
            if (string.IsNullOrEmpty(normalizedScope))
            {
                normalizedScope = string.Empty;
            }
        }

        var messages = await _conversationRepository.GetMessagesByThreadIdAsync(tenantId, threadId, page, pageSize, normalizedScope);
        return ServiceResult<List<ConversationMessage>>.Success(messages);
    }

    public async Task<ServiceResult<TopicsResult>> GetTopics(string threadId, int page, int pageSize)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;

            // Validate pagination parameters
            if (page <= 0)
            {
                return ServiceResult<TopicsResult>.BadRequest("Page number must be greater than 0. Pagination is 1-based.");
            }

            if (pageSize <= 0)
            {
                return ServiceResult<TopicsResult>.BadRequest("Page size must be greater than 0.");
            }

            if (pageSize > 100)
            {
                return ServiceResult<TopicsResult>.BadRequest("Page size cannot exceed 100.");
            }

            // OPTIMIZATION: Limit maximum page number to prevent deep pagination performance issues
            // For accessing topics beyond page 100, users should use search functionality
            const int MAX_PAGE = 100;
            if (page > MAX_PAGE)
            {
                return ServiceResult<TopicsResult>.BadRequest(
                    $"Page number cannot exceed {MAX_PAGE}. For accessing older topics, please use search functionality or contact support.");
            }

            _logger.LogDebug("GetTopics called for thread {ThreadId} with page={Page}, pageSize={PageSize}", 
                threadId, page, pageSize);

            var result = await _conversationRepository.GetTopicsByThreadIdAsync(tenantId, threadId, page, pageSize);
            
            _logger.LogDebug("GetTopics returned {Count} topics for thread {ThreadId}", result.Topics.Count, threadId);
            
            return ServiceResult<TopicsResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving topics for thread {ThreadId}", threadId);
            return ServiceResult<TopicsResult>.InternalServerError("An error occurred while retrieving topics");
        }
    }

    public async Task<ServiceResult<List<ConversationThread>>> GetThreads(string agent, int? page = null, int? pageSize = null)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var validatedAgentName = Agent.SanitizeAndValidateName(agent);
            
            // Validate pagination parameters - fail fast on invalid input
            if (page.HasValue && page.Value <= 0)
            {
                return ServiceResult<List<ConversationThread>>.BadRequest("Page number must be greater than 0. Pagination is 1-based.");
            }

            if (pageSize.HasValue && pageSize.Value <= 0)
            {
                return ServiceResult<List<ConversationThread>>.BadRequest("Page size must be greater than 0.");
            }

            // Ensure both page and pageSize are set if either is provided
            if (page.HasValue || pageSize.HasValue)
            {
                if (!page.HasValue)
                {
                    page = 1;
                }

                if (!pageSize.HasValue)
                {
                    pageSize = 10;
                }
            }

            _logger.LogDebug("GetThreads called for agent {Agent} with page={Page}, pageSize={PageSize}", 
                validatedAgentName, page, pageSize);

            var threads = await _conversationRepository.GetByTenantAndAgentAsync(tenantId, validatedAgentName, page, pageSize);
            
            _logger.LogDebug("GetThreads returned {Count} threads for agent {Agent}", threads.Count, validatedAgentName);
            
            return ServiceResult<List<ConversationThread>>.Success(threads);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving threads: {Message}", ex.Message);
            return ServiceResult<List<ConversationThread>>.BadRequest($"Validation failed: {ex.Message}");
        }
    }

    public async Task<ServiceResult<bool>> DeleteThread(string threadId)
    {
        try
        {
            // System admins can delete threads from any tenant, others can only delete from their own tenant
            var tenantId = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) 
                ? null 
                : _tenantContext.TenantId;
                
            var result = await _conversationRepository.DeleteThreadAsync(threadId, tenantId);
            if (!result)
            {
                return ServiceResult<bool>.NotFound("Thread not found or does not belong to the current tenant");
            }
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting thread {ThreadId}", threadId);
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the thread");
        }
    }
}