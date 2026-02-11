using Shared.Auth;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;
using System.Text.Json.Serialization;

namespace Shared.Services;

public class ChatOrDataRequest
{
    private string? _workflowType;
    private string _participantId = string.Empty;
    
    public string? Workflow { get; set; }
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageType? Type { get; set; }
    public string? RequestId { get; set; }
    
    // unique identifier for the participant, used to identify the participant in the message thread
    // Automatically normalized to lowercase for consistency (especially important for emails)
    public required string ParticipantId 
    { 
        get => _participantId;
        set => _participantId = value?.ToLowerInvariant() ?? string.Empty;
    }

    // unique identifier for the agent's workflow, used to identify the workflow in the message thread
    public string? WorkflowId { get; set; }

    // Scope for the message, used to identify the scope of the message thread
    public string? Scope { get; set; }

    // Hint for the agent to use when processing the message
    public string? Hint { get; set; }

    // Task identifier for the agent to track task execution
    public string? TaskId { get; set; }

    public string? WorkflowType 
    { 
        get 
        {
            if (!string.IsNullOrEmpty(_workflowType))
            {
                return _workflowType;
            }

            if (!string.IsNullOrEmpty(WorkflowId))
            {
                return WorkflowIdentifier.GetWorkflowType(WorkflowId);
            }

            if (!string.IsNullOrEmpty(Workflow))
            {
                return WorkflowIdentifier.GetWorkflowType(Workflow);
            }

            throw new InvalidOperationException("Unable to determine WorkflowType from WorkflowType or WorkflowId");
        }
        set 
        {
            _workflowType = value;
        }
    }
    
    public string GetAgent(ITenantContext tenantContext)
    { 
        var workflow = WorkflowId ?? WorkflowType ?? throw new Exception("WorkflowId or WorkflowType is required");
        return new WorkflowIdentifier(workflow, tenantContext).AgentName;
    }
    
    public object? Data { get; set; }
    public string? Text { get; set; }
    //public string? ThreadId { get; set; }
    public string? Authorization { get; set; }
    public string? Origin { get; set; }
}

public class HandoffRequest
{
    private string _participantId = string.Empty;
    
    public required string TargetWorkflowId { get; set; }
    public required string TargetWorkflowType { get; set; }
    public required string SourceAgent { get; set; }
    public required string SourceWorkflowType { get; set; }
    public required string SourceWorkflowId { get; set; }
    public required string ThreadId { get; set; }
    
    // Automatically normalized to lowercase for consistency (especially important for emails)
    public required string ParticipantId 
    { 
        get => _participantId;
        set => _participantId = value?.ToLowerInvariant() ?? string.Empty;
    }
    public string? Scope { get; set; }
    public required string Text { get; set; }
    public object? Data { get; set; }
    public string? Authorization { get; set; }
}


public interface IMessageService
{
    Task<ServiceResult<string>> ProcessIncomingMessage(ChatOrDataRequest request, MessageType messageType);
    Task<ServiceResult<string>> ProcessOutgoingMessage(ChatOrDataRequest request, MessageType messageType);
    Task<ServiceResult<string>> ProcessHandoff(HandoffRequest request);
    Task<ServiceResult<List<ConversationMessage>>> GetThreadHistoryAsync(string workflowId, string participantId, int page, int pageSize, string? scope, bool chatOnly = false, string sortOrder = "desc");
    Task<ServiceResult<List<ConversationMessage>>> GetThreadHistoryAsync(string threadId, int page, int pageSize, string? scope = null, bool chatOnly = false);
    Task<ServiceResult<bool>> DeleteThreadAsync(string workflowId, string participantId);
    Task<ServiceResult<bool>> DeleteMessagesByTopicAsync(string workflowId, string participantId, string? topic);
    Task<ServiceResult<string?>> GetLastTaskIdAsync(string workflowId, string participantId, string? scope = null);
    Task<ServiceResult<TopicsResult>> GetTopicsByWorkflowAndParticipantAsync(string workflowId, string participantId, int page, int pageSize);
}

public class MessageService : IMessageService
{

    private readonly ILogger<MessageService> _logger;
    private readonly ITenantContext _tenantContext;

    private readonly IConversationRepository _conversationRepository;
    private readonly IWorkflowSignalService _workflowSignalService;

        public MessageService(
        ILogger<MessageService> logger,
        ITenantContext tenantContext,
        IConversationRepository conversationRepository,
        IWorkflowSignalService workflowSignalService
        )
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _conversationRepository = conversationRepository;
        _workflowSignalService = workflowSignalService;
    }

    public async Task<ServiceResult<string>> ProcessHandoff(HandoffRequest request)
    {
        _logger.LogInformation("Processing handover for thread {ThreadId}", request.ThreadId);

        try
        {
            if (request.ThreadId == null)
            {
                throw new ArgumentNullException(nameof(request.ThreadId), "ThreadId is required to handover a conversation");
            }

            // Add the tenantId to the workflowId
            if (!request.TargetWorkflowId.StartsWith(_tenantContext.TenantId + ":"))
            {
                request.TargetWorkflowId = $"{_tenantContext.TenantId}:{request.TargetWorkflowId}";
            }

            // Instead of updating the existing thread's workflow type (which might violate unique constraints),
            // we should create or get a thread for the target workflow type
            var targetThread = new ConversationThread
            {
                TenantId = _tenantContext.TenantId,
                WorkflowId = request.TargetWorkflowId,
                WorkflowType = request.TargetWorkflowType,
                Agent = request.SourceAgent,
                ParticipantId = request.ParticipantId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = _tenantContext.LoggedInUser,
                Status = ConversationThreadStatus.Active
            };

            // This will either create a new thread or return the existing one
            var targetThreadId = await _conversationRepository.CreateOrGetThreadIdAsync(targetThread);

            var messageRequest = new ChatOrDataRequest
            {
                ParticipantId = request.ParticipantId,
                WorkflowId = request.TargetWorkflowId,
                WorkflowType = request.TargetWorkflowType,
                Text = $"{request.SourceWorkflowType} -> {request.TargetWorkflowType}",
                Data = request.Data,
                Authorization = request.Authorization
            };

            await SaveMessage(targetThreadId, messageRequest, MessageDirection.Outgoing, MessageType.Handoff);

            messageRequest.Text = request.Text;
            //await SignalWorkflowAsync(messageRequest);
            await ProcessIncomingMessage(new ChatOrDataRequest
            {
                ParticipantId = request.ParticipantId,
                WorkflowId = request.TargetWorkflowId,
                WorkflowType = request.TargetWorkflowType,
                Text = request.Text,
                Data = request.Data,
                Scope = request.Scope,
                Authorization = request.Authorization
            }, MessageType.Chat);

            return ServiceResult<string>.Success(targetThreadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing handover");
            throw;
        }
    }

    public async Task<ServiceResult<List<ConversationMessage>>> GetThreadHistoryAsync(string threadId, int page, int pageSize, string? scope, bool chatOnly = false)
    {
        if (page < 1 || pageSize < 1)
        {
            _logger.LogWarning("Invalid request: page {Page} and pageSize {PageSize} must be greater than 0", page, pageSize);
            return ServiceResult<List<ConversationMessage>>.BadRequest("Page and PageSize must be greater than 0");
        }
        var messages = await _conversationRepository.GetMessagesByThreadIdAsync(_tenantContext.TenantId, threadId, page, pageSize, scope, chatOnly);
        return ServiceResult<List<ConversationMessage>>.Success(messages);
    }

    public async Task<ServiceResult<List<ConversationMessage>>> GetThreadHistoryAsync(string workflowId, string participantId, int page, int pageSize, string? scope, bool chatOnly = false, string sortOrder = "desc")
    {
        try
        {
            _logger.LogInformation("Getting message history for workflowId {WorkflowId}, participant {ParticipantId}, page {Page}, pageSize {PageSize}, sortOrder {SortOrder}",
                workflowId, participantId, page, pageSize, sortOrder);

            if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields workflowId {WorkflowId}, participant {ParticipantId}", workflowId, participantId);
                return ServiceResult<List<ConversationMessage>>.BadRequest("WorkflowId and ParticipantId are required");
            }

            if (string.IsNullOrEmpty(workflowId))
            {
                _logger.LogWarning("Invalid request: missing required fields workflowId {WorkflowId}", workflowId);
                return ServiceResult<List<ConversationMessage>>.BadRequest("WorkflowId is required");
            }

            if (page < 1 || pageSize < 1)
            {
                _logger.LogWarning("Invalid request: page {Page} and pageSize {PageSize} must be greater than 0", page, pageSize);
                return ServiceResult<List<ConversationMessage>>.BadRequest("Page and PageSize must be greater than 0");
            }

            // Get messages directly by workflow and participant IDs
            var messages = await _conversationRepository.GetMessagesByWorkflowAndParticipantAsync(workflowId, participantId, page, pageSize, scope, sortOrder);

            _logger.LogInformation("Found {Count} messages for workflowId {WorkflowId}, participant {ParticipantId}", messages.Count, workflowId, participantId);

            return ServiceResult<List<ConversationMessage>>.Success(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting message history for workflowId {WorkflowId}, participant {ParticipantId}", workflowId, participantId);
            throw;
        }
    }

    public async Task<ServiceResult<string>> ProcessOutgoingMessage(ChatOrDataRequest request, MessageType messageType)
    {
        _logger.LogInformation("Processing outbound message from workflow {WorkflowId} to participant {ParticipantId}",
             request.WorkflowId, request.ParticipantId);

        try
        {
            // Generate RequestId if not provided for request tracking and correlation
            if (string.IsNullOrEmpty(request.RequestId))
            {
                request.RequestId = MessageRequestProcessor.GenerateRequestId(
                    request.WorkflowId ?? $"{_tenantContext.TenantId}:{request.WorkflowType}", 
                    request.ParticipantId);
            }

            var threadId = await CreateOrGetThread(request);

            // Auto-populate origin and platform metadata from last incoming message if not provided
            // This enables automatic routing back to external platforms (Slack, Teams, etc.)
            if (string.IsNullOrEmpty(request.Origin))
            {
                var lastOrigin = await _conversationRepository.GetLastIncomingOriginAsync(threadId, _tenantContext.TenantId);
                if (!string.IsNullOrEmpty(lastOrigin))
                {
                    request.Origin = lastOrigin;
                    _logger.LogInformation("Auto-populated origin from last incoming message: {Origin}", lastOrigin);
                }
            }

            // Auto-populate platform-specific metadata (e.g., Slack channel, Teams conversation) if not provided
            if (request.Data == null && !string.IsNullOrEmpty(request.Origin) && request.Origin.StartsWith("app:"))
            {
                var lastData = await _conversationRepository.GetLastIncomingDataAsync(threadId, _tenantContext.TenantId);
                if (lastData != null)
                {
                    request.Data = lastData;
                    _logger.LogInformation("Auto-populated platform metadata from last incoming message");
                }
            }

            var message = await SaveMessage(threadId, request, MessageDirection.Outgoing, messageType);

            return ServiceResult<string>.Success(message.ThreadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing outbound message");
            throw;
        }
    }

    public async Task<ServiceResult<string>> ProcessIncomingMessage(ChatOrDataRequest request, MessageType messageType)
    {
        if (request.WorkflowId == null && request.WorkflowType == null)
        {
            throw new Exception("WorkflowId or WorkflowType is required");
        }

        if (request.WorkflowId == null && request.WorkflowType != null)
        {
            ExtractWorkflowId(request);
        }

        if (request.Authorization == null && _tenantContext.Authorization != null)
        {
            request.Authorization = _tenantContext.Authorization;
        }

        // Generate RequestId if not provided for request tracking and correlation
        if (string.IsNullOrEmpty(request.RequestId))
        {
            request.RequestId = MessageRequestProcessor.GenerateRequestId(
                request.WorkflowId!, 
                request.ParticipantId);
        }

        // WorkflowId cant start or end with ':'
        if (request.WorkflowId!.StartsWith(':') || request.WorkflowId!.EndsWith(':'))
        {
            throw new Exception("WorkflowId cant start or end with ':'");
        }

        //check if workflowId contains colons but doesn't start with tenantId
        if (request.WorkflowId!.Contains(':') && !request.WorkflowId.StartsWith(_tenantContext.TenantId + ":"))
        {
            throw new Exception("WorkflowId must start with tenantId. WorkflowId: " + request.WorkflowId);
        }
        //if workflowId doesn't contain colons, add tenantId to the beginning
        if (!string.IsNullOrEmpty(request.WorkflowId) && !request.WorkflowId!.Contains(':'))
        {
            request.WorkflowId = $"{_tenantContext.TenantId}:{request.WorkflowType}:{request.WorkflowId}";
        }

        _logger.LogInformation("Processing inbound message for WorkflowId `{WorkflowId}` from participant {ParticipantId}",
            request.WorkflowId, request.ParticipantId);
        
        // Critical Operation: If the threadId is not provided, we need to create a new thread
        var threadId = await CreateOrGetThread(request);

        // Save the message
        await SaveMessage(threadId, request, MessageDirection.Incoming, messageType);

        // Signal the workflow
        await SignalWorkflowAsync(threadId, request, messageType);

        _logger.LogInformation("Successfully processed inbound message");

        return ServiceResult<string>.Success(threadId);
    }

    private void ExtractWorkflowId(ChatOrDataRequest request)
    {
        if (!string.IsNullOrEmpty(request.WorkflowId))
        {
            return;
        }

        if (request.WorkflowType == null)
        {
            throw new Exception("WorkflowType is required when WorkflowId is not provided");
        }
        request.WorkflowId = $"{_tenantContext.TenantId}:{request.WorkflowType}";
    }



    private async Task SignalWorkflowAsync(string threadId,ChatOrDataRequest request, MessageType messageType)
    {
        if (request.WorkflowType == null)
        {
            throw new Exception("WorkflowType is required");
        }

        var agent = request.WorkflowType.Split(":").FirstOrDefault()?.Trim() ?? throw new Exception("WorkflowType should be in the format of <agent>:<workflowType>");
        var signalRequest = new WorkflowSignalWithStartRequest
        {
            SignalName = Constants.SIGNAL_INBOUND_CHAT_OR_DATA,
            TargetWorkflowId = request.WorkflowId,
            TargetWorkflowType = request.WorkflowType,            
            SourceAgent = agent,
            Payload = new {
                 Agent = agent,
                 ThreadId = threadId,
                 request.ParticipantId,
                 request.Text, 
                 request.RequestId,
                 request.Scope,
                 request.Hint,
                 request.TaskId,
                 request.Data,
                 Type = messageType.ToString(),
                 request.Authorization
            }
        };
        await _workflowSignalService.SignalWithStartWorkflow(signalRequest);
    }

    private async Task<string> CreateOrGetThread(ChatOrDataRequest request)
    {
        if (request.WorkflowType == null)
        {
            throw new Exception("WorkflowType is required");
        }

        var agent = request.WorkflowType.Split(":").FirstOrDefault()?.Trim() ?? throw new Exception("WorkflowType should be in the format of <agent>:<workflowType>");
        var thread = new ConversationThread
        {
            TenantId = _tenantContext.TenantId,
            WorkflowId = request.WorkflowId ?? $"{_tenantContext.TenantId}:{request.WorkflowType}",
            WorkflowType = request.WorkflowType,
            Agent = agent,
            ParticipantId = request.ParticipantId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            Status = ConversationThreadStatus.Active
        };

        var threadId = await _conversationRepository.CreateOrGetThreadIdAsync(thread);
        return threadId;
    }
    
    private async Task<ConversationMessage> SaveMessage(string threadId,ChatOrDataRequest request, MessageDirection direction, MessageType messageType)
    {
        // Normalize empty scope to null (empty string and null both represent the default topic)
        var normalizedScope = string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim();

        var message = new ConversationMessage
        {
            ThreadId = threadId,
            ParticipantId = request.ParticipantId,
            TenantId = _tenantContext.TenantId,
            Hint = request.Hint,
            TaskId = request.TaskId,
            Scope = normalizedScope,  // Use normalized scope
            RequestId = request.RequestId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            Direction = direction,
            Text = request.Text,
            Data = request.Data, // Assign original metadata
            WorkflowId = request.WorkflowId ?? $"{_tenantContext.TenantId}:{request.WorkflowType}",
            WorkflowType = request.WorkflowType ?? throw new Exception("WorkflowType is required"),
            MessageType = messageType,
            Origin = request.Origin

        };

        // Save the message with transaction support
        message.Id = await _conversationRepository.SaveMessageAsync(message);
        _logger.LogInformation("Created conversation message {MessageId} in thread {ThreadId}", message.Id, message.ThreadId);

        return message;
    }

    public async Task<ServiceResult<bool>> DeleteThreadAsync(string workflowId, string participantId)
    {
        try
        {
            _logger.LogInformation("Attempting to delete thread for workflowId {WorkflowId}, participant {ParticipantId}", 
                workflowId, participantId);

            if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields workflowId {WorkflowId}, participant {ParticipantId}", 
                    workflowId, participantId);
                return ServiceResult<bool>.BadRequest("WorkflowId and ParticipantId are required");
            }

            // First, find the thread ID using workflowId and participantId
            string threadId;
            try
            {
                threadId = await _conversationRepository.GetThreadIdAsync(_tenantContext.TenantId, workflowId, participantId);
            }
            catch (KeyNotFoundException)
            {
                _logger.LogWarning("Thread not found for workflowId {WorkflowId}, participant {ParticipantId}, tenant {TenantId}", 
                    workflowId, participantId, _tenantContext.TenantId);
                return ServiceResult<bool>.NotFound("Thread not found");
            }

            // Delete all messages in the thread first
            await _conversationRepository.DeleteMessagesByThreadIdAsync(threadId);
            _logger.LogInformation("Deleted messages for thread {ThreadId}", threadId);

            // Delete the thread
            var result = await _conversationRepository.DeleteThreadAsync(threadId, _tenantContext.TenantId);
            
            if (!result)
            {
                _logger.LogWarning("Failed to delete thread {ThreadId} for workflowId {WorkflowId}, participant {ParticipantId}", 
                    threadId, workflowId, participantId);
                return ServiceResult<bool>.InternalServerError("Failed to delete thread");
            }

            _logger.LogInformation("Successfully deleted thread {ThreadId} for workflowId {WorkflowId}, participant {ParticipantId}", 
                threadId, workflowId, participantId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting thread for workflowId {WorkflowId}, participant {ParticipantId}", 
                workflowId, participantId);
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the thread");
        }
    }

    public async Task<ServiceResult<bool>> DeleteMessagesByTopicAsync(string workflowId, string participantId, string? topic)
    {
        try
        {
            _logger.LogInformation("Attempting to delete messages for workflowId {WorkflowId}, participant {ParticipantId}, topic {Topic}", 
                workflowId, participantId, topic ?? "null");

            if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields workflowId {WorkflowId}, participant {ParticipantId}", 
                    workflowId, participantId);
                return ServiceResult<bool>.BadRequest("WorkflowId and ParticipantId are required");
            }

            // Delete messages by workflow, participant, and scope (topic)
            var result = await _conversationRepository.DeleteMessagesByWorkflowParticipantAndScopeAsync(
                _tenantContext.TenantId, workflowId, participantId, topic);

            if (!result)
            {
                _logger.LogWarning("Failed to delete messages for workflowId {WorkflowId}, participant {ParticipantId}, topic {Topic}", 
                    workflowId, participantId, topic ?? "null");
                return ServiceResult<bool>.InternalServerError("Failed to delete messages");
            }

            _logger.LogInformation("Successfully deleted messages for workflowId {WorkflowId}, participant {ParticipantId}, topic {Topic}", 
                workflowId, participantId, topic ?? "null");
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting messages for workflowId {WorkflowId}, participant {ParticipantId}, topic {Topic}", 
                workflowId, participantId, topic ?? "null");
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting messages");
        }
    }

    public async Task<ServiceResult<string?>> GetLastTaskIdAsync(string workflowId, string participantId, string? scope = null)
    {
        try
        {
            _logger.LogInformation("Getting last task id for workflowId {WorkflowId}, participant {ParticipantId}, scope {Scope}",
                workflowId, participantId, scope ?? "null");

            if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields workflowId {WorkflowId}, participant {ParticipantId}",
                    workflowId, participantId);
                return ServiceResult<string?>.BadRequest("WorkflowId and ParticipantId are required");
            }

            var taskId = await _conversationRepository.GetLastTaskIdAsync(_tenantContext.TenantId, workflowId, participantId, scope);

            if (taskId == null)
            {
                _logger.LogInformation("No task id found for workflowId {WorkflowId}, participant {ParticipantId}, scope {Scope}",
                    workflowId, participantId, scope ?? "null");
            }
            else
            {
                _logger.LogInformation("Found task id for workflowId {WorkflowId}, participant {ParticipantId}, scope {Scope}",
                    workflowId, participantId, scope ?? "null");
            }

            return ServiceResult<string?>.Success(taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting last task id for workflowId {WorkflowId}, participant {ParticipantId}",
                workflowId, participantId);
            return ServiceResult<string?>.InternalServerError("An error occurred while getting the last task id");
        }
    }

    public async Task<ServiceResult<TopicsResult>> GetTopicsByWorkflowAndParticipantAsync(string workflowId, string participantId, int page, int pageSize)
    {
        try
        {
            _logger.LogInformation("Getting topics for workflowId {WorkflowId}, participant {ParticipantId}, page {Page}, pageSize {PageSize}",
                workflowId, participantId, page, pageSize);

            if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields workflowId {WorkflowId}, participant {ParticipantId}", 
                    workflowId, participantId);
                return ServiceResult<TopicsResult>.BadRequest("WorkflowId and ParticipantId are required");
            }

            if (page < 1 || pageSize < 1)
            {
                _logger.LogWarning("Invalid request: page {Page} and pageSize {PageSize} must be greater than 0", page, pageSize);
                return ServiceResult<TopicsResult>.BadRequest("Page and PageSize must be greater than 0");
            }

            // Get the thread ID using workflowId and participantId
            string threadId;
            try
            {
                threadId = await _conversationRepository.GetThreadIdAsync(_tenantContext.TenantId, workflowId, participantId);
            }
            catch (KeyNotFoundException)
            {
                _logger.LogInformation("Thread not found for workflowId {WorkflowId}, participant {ParticipantId}, tenant {TenantId}. Returning empty topics list.", 
                    workflowId, participantId, _tenantContext.TenantId);
                
                // Return empty result when thread doesn't exist
                return ServiceResult<TopicsResult>.Success(new TopicsResult
                {
                    Topics = new List<TopicInfo>
                    {
                        new TopicInfo
                        {
                            Scope = null,
                            MessageCount = 0
                        }
                    },
                    Pagination = new PaginationMetadata
                    {
                        CurrentPage = page,
                        PageSize = pageSize,
                        TotalTopics = 1,
                        TotalPages = 1,
                        HasMore = false
                    }
                });
            }

            // Get topics for the thread
            var topicsResult = await _conversationRepository.GetTopicsByThreadIdAsync(_tenantContext.TenantId, threadId, page, pageSize);

            _logger.LogInformation("Found {Count} topics for workflowId {WorkflowId}, participant {ParticipantId}", 
                topicsResult.Topics.Count, workflowId, participantId);

            return ServiceResult<TopicsResult>.Success(topicsResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting topics for workflowId {WorkflowId}, participant {ParticipantId}", 
                workflowId, participantId);
            return ServiceResult<TopicsResult>.InternalServerError("An error occurred while retrieving topics");
        }
    }

}