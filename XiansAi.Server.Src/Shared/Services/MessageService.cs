using Shared.Auth;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

public class ChatOrDataRequest
{
    public required string ParticipantId { get; set; }
    public required string WorkflowId { get; set; }
    public required string WorkflowType { get; set; }
    public required string Agent { get; set; }
    public object? Data { get; set; }
    public string? Text { get; set; }
    public string? ThreadId { get; set; }
    public string? Authorization { get; set; }
}

public class HandoffRequest
{
    public required string TargetWorkflowId { get; set; }
    public required string TargetWorkflowType { get; set; }
    public required string SourceAgent { get; set; }
    public required string SourceWorkflowType { get; set; }
    public required string SourceWorkflowId { get; set; }
    public required string ThreadId { get; set; }
    public required string ParticipantId { get; set; }
    public required string Text { get; set; }
    public object? Data { get; set; }
    public string? Authorization { get; set; }
}


public interface IMessageService
{
    Task<ServiceResult<string>> ProcessIncomingMessage(ChatOrDataRequest request, MessageType messageType);
    Task<ServiceResult<string>> ProcessOutgoingMessage(ChatOrDataRequest request, MessageType messageType);
    Task<ServiceResult<string>> ProcessHandoff(HandoffRequest request);
    Task<ServiceResult<List<ConversationMessage>>> GetThreadHistoryAsync(string workflowType, string participantId, int page, int pageSize, bool includeMetadata = false);
    Task<ServiceResult<string>> GetAuthorization(string authorizationGuid);
}

public class MessageService : IMessageService
{

    private readonly ILogger<MessageService> _logger;
    private readonly ITenantContext _tenantContext;

    private readonly IConversationThreadRepository _threadRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IWorkflowSignalService _workflowSignalService;
    private readonly IAuthorizationCacheService _authorizationCacheService;

        public MessageService(
        ILogger<MessageService> logger,
        ITenantContext tenantContext,
        IConversationThreadRepository threadRepository,
        IConversationMessageRepository messageRepository,
        IWorkflowSignalService workflowSignalService,
        IAuthorizationCacheService authorizationCacheService
        )
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _threadRepository = threadRepository;
        _messageRepository = messageRepository;
        _workflowSignalService = workflowSignalService;
        _authorizationCacheService = authorizationCacheService;
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

            // the workflowid should not start with "<tenantId>:"
            if (request.TargetWorkflowId.StartsWith(_tenantContext.TenantId + ":"))
            {
                throw new ArgumentException("WorkflowId submitted for handover cannot start with '<tenantId>:'. Remove the tenantId from the workflowId.");
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
            var targetThreadId = await _threadRepository.CreateOrGetAsync(targetThread);

            var messageRequest = new ChatOrDataRequest
            {
                ThreadId = targetThreadId,  // Use the target thread ID
                ParticipantId = request.ParticipantId,
                WorkflowId = request.TargetWorkflowId,
                WorkflowType = request.TargetWorkflowType,
                Text = $"{request.SourceWorkflowType} -> {request.TargetWorkflowType}",
                Data = request.Data,
                Agent = request.SourceAgent,
                Authorization = request.Authorization
            };

            await SaveMessage(messageRequest, MessageDirection.Outgoing, MessageType.Handoff);

            messageRequest.Text = request.Text;
            //await SignalWorkflowAsync(messageRequest);
            await ProcessIncomingMessage(new ChatOrDataRequest
            {
                ThreadId = targetThreadId,  // Use the target thread ID
                ParticipantId = request.ParticipantId,
                WorkflowId = request.TargetWorkflowId,
                WorkflowType = request.TargetWorkflowType,
                Text = request.Text,
                Data = request.Data,
                Agent = request.SourceAgent,
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

    public async Task<ServiceResult<List<ConversationMessage>>> GetThreadHistoryAsync(string workflowType, string participantId, int page, int pageSize, bool includeMetadata = false)
    {
        try
        {
            _logger.LogInformation("Getting message history for workflowType {WorkflowType}, participant {ParticipantId}, page {Page}, pageSize {PageSize}",
                workflowType, participantId, page, pageSize);

            if (string.IsNullOrEmpty(workflowType) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields workflowType {WorkflowType}, participant {ParticipantId}", workflowType, participantId);
                return ServiceResult<List<ConversationMessage>>.BadRequest("WorkflowType and ParticipantId are required");
            }

            if (string.IsNullOrEmpty(workflowType))
            {
                _logger.LogWarning("Invalid request: missing required fields workflowType {WorkflowType}", workflowType);
                return ServiceResult<List<ConversationMessage>>.BadRequest("WorkflowType is required");
            }

            if (page < 1 || pageSize < 1)
            {
                _logger.LogWarning("Invalid request: page {Page} and pageSize {PageSize} must be greater than 0", page, pageSize);
                return ServiceResult<List<ConversationMessage>>.BadRequest("Page and PageSize must be greater than 0");
            }

            // Get messages directly by workflow and participant IDs
            var messages = await _messageRepository.GetByAgentAndParticipantAsync(_tenantContext.TenantId, workflowType, participantId, page, pageSize, includeMetadata );

            _logger.LogInformation("Found {Count} messages for workflowType {WorkflowType} and participant {ParticipantId}",
                messages.Count, workflowType, participantId);

            return ServiceResult<List<ConversationMessage>>.Success(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting message history for workflowType {WorkflowType}, participant {ParticipantId}", workflowType, participantId);
            throw;
        }
    }

    public async Task<ServiceResult<string>> ProcessOutgoingMessage(ChatOrDataRequest request, MessageType messageType)
    {
        _logger.LogInformation("Processing outbound message from workflow {WorkflowId} to participant {ParticipantId}",
             request.WorkflowId, request.ParticipantId);

        try
        {
            if (request.ThreadId == null)
            {
                request.ThreadId = await CreateOrGetThread(request);
            }

            var message = await SaveMessage(request, MessageDirection.Outgoing, messageType);

            // TODO: Notify webhooks
            //await NotifyWebhooksAsync(message);

            return ServiceResult<string>.Success(request.ThreadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing outbound message");
            throw;
        }
    }

    public async Task<ServiceResult<string>> ProcessIncomingMessage(ChatOrDataRequest request, MessageType messageType)
    {
        _logger.LogInformation("Processing inbound message for agent {AgentId} from participant {ParticipantId}",
            request.WorkflowId, request.ParticipantId);
        
        await HandleAuthorization(request);

        if (request.ThreadId == null)
        {
            request.ThreadId = await CreateOrGetThread(request);
        }

        // Save the message
        await SaveMessage(request, MessageDirection.Incoming, messageType);

        // Signal the workflow
        await SignalWorkflowAsync(request);

        _logger.LogInformation("Successfully processed inbound message");

        return ServiceResult<string>.Success(request.ThreadId);
    }

    private async Task HandleAuthorization(ChatOrDataRequest request)
    {
        if (request.Authorization != null)
        {
            var authorizationGuid = await _authorizationCacheService.CacheAuthorization(request.Authorization);
            request.Authorization = authorizationGuid;
        }
    }

    private async Task SignalWorkflowAsync(ChatOrDataRequest request)
    {
        var agent = request.WorkflowType.Split(":").FirstOrDefault() ?? throw new Exception("WorkflowType should be in the format of <agent>:<workflowType>");
        var signalRequest = new WorkflowSignalWithStartRequest
        {
            SignalName = Constants.SIGNAL_INBOUND_CHAT_OR_DATA,
            TargetWorkflowId = request.WorkflowId,
            TargetWorkflowType = request.WorkflowType,            
            SourceAgent = agent,
            Payload = new {
                 Agent = agent,
                 request.ThreadId,
                 request.ParticipantId,
                 request.Text, 
                 request.Data,
                 request.Authorization
            }
        };
        await _workflowSignalService.SignalWithStartWorkflow(signalRequest);
    }

    private async Task<string> CreateOrGetThread(ChatOrDataRequest request)
    {
        var agent = request.WorkflowType.Split(":").FirstOrDefault() ?? throw new Exception("WorkflowType should be in the format of <agent>:<workflowType>");
        var thread = new ConversationThread
        {
            TenantId = _tenantContext.TenantId,
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType,
            Agent = agent,
            ParticipantId = request.ParticipantId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            Status = ConversationThreadStatus.Active
        };

        var threadId = await _threadRepository.CreateOrGetAsync(thread);
        return threadId;
    }
    
    private async Task<ConversationMessage> SaveMessage(ChatOrDataRequest request, MessageDirection direction, MessageType messageType)
    {
        if (request.ThreadId == null)
        {
            throw new Exception("ThreadId is required");
        }

        var message = new ConversationMessage
        {
            ThreadId = request.ThreadId,
            ParticipantId = request.ParticipantId,
            TenantId = _tenantContext.TenantId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            Direction = direction,
            Text = request.Text,
            Data = request.Data, // Assign original metadata
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType,
            MessageType = messageType
        };

        // This call will modify message.Metadata within the 'message' instance to be a BsonDocument
        message.Id = await _messageRepository.CreateAndUpdateThreadAsync(message, request.ThreadId, DateTime.UtcNow);
        _logger.LogInformation("Created conversation message {MessageId} in thread {ThreadId}", message.Id, request.ThreadId);

        return message;
    }

    public async Task<ServiceResult<string>> GetAuthorization(string authorizationGuid)
    {
       var authorization = await _authorizationCacheService.GetAuthorization(authorizationGuid);
       if (authorization == null)
       {
        return ServiceResult<string>.NotFound("Authorization not found");
       }
       return ServiceResult<string>.Success(authorization);
    }
}