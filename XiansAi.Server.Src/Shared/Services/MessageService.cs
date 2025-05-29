using Shared.Auth;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

public class MessageRequest
{
    public required string ParticipantId { get; set; }
    public required string WorkflowId { get; set; }
    public required string WorkflowType { get; set; }
    public required string Agent { get; set; }
    public object? Metadata { get; set; }
    public required string Content { get; set; }
    public string? ThreadId { get; set; }
    public string? QueueName { get; set; }
    public string? Assignment { get; set; }
}

public class HandoverRequest
{
    public required string WorkflowId { get; set; }
    public required string WorkflowType { get; set; }
    public required string Agent { get; set; }
    public required string ThreadId { get; set; }
    public required string FromWorkflowType { get; set; }
    public required string ParticipantId { get; set; }
    public required string UserRequest { get; set; }
}

public interface IMessageService
{
    Task<ServiceResult<string>> ProcessIncomingMessage(MessageRequest request);
    Task<ServiceResult<string>> ProcessOutgoingMessage(MessageRequest request);
    Task<ServiceResult<string>> ProcessHandover(HandoverRequest request);
    Task<ServiceResult<List<ConversationMessage>>> GetThreadHistoryAsync(string agent, string workflowType, string participantId, int page, int pageSize);
    Task<ServiceResult<ConversationMessage>> GetLatestConversationMessageAsync(string threadId, string agent, string workflowType, string participantId, string workflowId);
}

public class MessageService : IMessageService
{

    private readonly ILogger<MessageService> _logger;
    private readonly ITenantContext _tenantContext;

    private readonly IConversationThreadRepository _threadRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IConversationChangeListener _conversationChangeListener;
    private readonly IWorkflowSignalService _workflowSignalService;

        public MessageService(
        ILogger<MessageService> logger,
        ITenantContext tenantContext,
        IConversationThreadRepository threadRepository,
        IConversationMessageRepository messageRepository,
        IWorkflowSignalService workflowSignalService,
        IConversationChangeListener conversationChangeListener
        )
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _threadRepository = threadRepository;
        _messageRepository = messageRepository;
        _workflowSignalService = workflowSignalService;
        _conversationChangeListener = conversationChangeListener;
    }

    public async Task<ServiceResult<string>> ProcessHandover(HandoverRequest request)
    {
        _logger.LogInformation("Processing handover for thread {ThreadId}", request.ThreadId);

        try
        {
            if (request.ThreadId == null)
            {
                throw new ArgumentNullException(nameof(request.ThreadId), "ThreadId is required to handover a conversation");
            }

            // the workflowid should not start with "<tenantId>:"
            if (request.WorkflowId.StartsWith(_tenantContext.TenantId + ":"))
            {
                throw new ArgumentException("WorkflowId submitted for handover cannot start with '<tenantId>:'. Remove the tenantId from the workflowId.");
            }

            // Add the tenantId to the workflowId
            request.WorkflowId = $"{_tenantContext.TenantId}:{request.WorkflowId}";

            // Instead of updating the existing thread's workflow type (which might violate unique constraints),
            // we should create or get a thread for the target workflow type
            var targetThread = new ConversationThread
            {
                TenantId = _tenantContext.TenantId,
                WorkflowId = request.WorkflowId,
                WorkflowType = request.WorkflowType,
                Agent = request.Agent,
                ParticipantId = request.ParticipantId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = _tenantContext.LoggedInUser,
                Status = ConversationThreadStatus.Active,
                IsInternalThread = false
            };

            // This will either create a new thread or return the existing one
            var targetThreadId = await _threadRepository.CreateOrGetAsync(targetThread);

            var messageRequest = new MessageRequest
            {
                ThreadId = targetThreadId,  // Use the target thread ID
                ParticipantId = request.ParticipantId,
                WorkflowId = request.WorkflowId,
                WorkflowType = request.WorkflowType,
                Agent = request.Agent,
                Content = $"{request.FromWorkflowType} -> {request.WorkflowType}",
                Metadata = new {
                    HandoverInitiated = true,
                    SourceThreadId = request.ThreadId  // Keep track of the source thread
                }
            };

            await SaveMessage(messageRequest, MessageDirection.Handover);

            messageRequest.Content = request.UserRequest;
            //await SignalWorkflowAsync(messageRequest);
            await ProcessIncomingMessage(new MessageRequest
            {
                ThreadId = targetThreadId,  // Use the target thread ID
                ParticipantId = request.ParticipantId,
                WorkflowId = request.WorkflowId,
                WorkflowType = request.WorkflowType,
                Agent = request.Agent,
                Content = request.UserRequest,
                Metadata = new {
                    HandoverFrom = request.FromWorkflowType,
                    SourceThreadId = request.ThreadId  // Keep track of the source thread
                }
            });

            return ServiceResult<string>.Success(targetThreadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing handover");
            throw;
        }
    }

    public async Task<ServiceResult<List<ConversationMessage>>> GetThreadHistoryAsync(string agent, string workflowType, string participantId, int page, int pageSize)
    {
        try
        {
            _logger.LogInformation("Getting message history for agent {Agent}, participant {ParticipantId}, page {Page}, pageSize {PageSize}",
                agent, participantId, page, pageSize);

            if (string.IsNullOrEmpty(agent) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields");
                return ServiceResult<List<ConversationMessage>>.BadRequest("Agent and ParticipantId are required");
            }

            if (string.IsNullOrEmpty(workflowType))
            {
                _logger.LogWarning("Invalid request: missing required fields");
                return ServiceResult<List<ConversationMessage>>.BadRequest("WorkflowType is required");
            }

            if (page < 1 || pageSize < 1)
            {
                _logger.LogWarning("Invalid request: page and pageSize must be greater than 0");
                return ServiceResult<List<ConversationMessage>>.BadRequest("Page and PageSize must be greater than 0");
            }

            // Get messages directly by workflow and participant IDs
            var messages = await _messageRepository.GetByAgentAndParticipantAsync(_tenantContext.TenantId, agent, workflowType, participantId, page, pageSize);

            _logger.LogInformation("Found {Count} messages for agent {Agent} and participant {ParticipantId}",
                messages.Count, agent, participantId);

            return ServiceResult<List<ConversationMessage>>.Success(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting message history for agent {Agent}, participant {ParticipantId}", agent, participantId);
            throw;
        }
    }

    public async Task<ServiceResult<string>> ProcessOutgoingMessage(MessageRequest request)
    {
        _logger.LogInformation("Processing outbound message from workflow {WorkflowId} to participant {ParticipantId}",
             request.WorkflowId, request.ParticipantId);

        try
        {
            if (request.ThreadId == null)
            {
                request.ThreadId = await CreateThread(request);
            }

            var message = await SaveMessage(request, MessageDirection.Outgoing);

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

    public async Task<ServiceResult<string>> ProcessIncomingMessage(MessageRequest request)
    {
        _logger.LogInformation("Processing inbound message for agent {AgentId} from participant {ParticipantId}",
            request.WorkflowId, request.ParticipantId);
        
        if (request.ThreadId == null)
        {
            request.ThreadId = await CreateThread(request);
        }

        // Save the message
        var message = await SaveMessage(request, MessageDirection.Incoming);

        // Signal the workflow
        await SignalWorkflowAsync(request);

        _logger.LogInformation("Successfully processed inbound message");

        return ServiceResult<string>.Success(request.ThreadId);
    }

    private async Task SignalWorkflowAsync(MessageRequest request)
    {
        var signalRequest = new WorkflowSignalWithStartRequest
        {
            SignalName = Constants.SIGNAL_INBOUND_MESSAGE,
            TargetWorkflowId = request.WorkflowId,
            TargetWorkflowType = request.WorkflowType,            
            SourceAgent = request.Agent,
            Payload = new {
                 request.Agent,
                 request.ThreadId,
                 request.ParticipantId,
                 request.Content, 
                 request.Metadata
            }
        };
        await _workflowSignalService.SignalWithStartWorkflow(signalRequest);
    }

    private async Task<string> CreateThread(MessageRequest request)
    {
        var thread = new ConversationThread
        {
            TenantId = _tenantContext.TenantId,
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType,
            Agent = request.Agent,
            ParticipantId = request.ParticipantId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            Status = ConversationThreadStatus.Active,
            IsInternalThread = false
        };

        var threadId = await _threadRepository.CreateOrGetAsync(thread);
        return threadId;
    }
    

    private async Task<ConversationMessage> SaveMessage(MessageRequest request, MessageDirection direction)
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
            Content = request.Content,
            Metadata = request.Metadata,
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType
        };

        // Save message to database and update thread in a single transaction
        message.Id = await _messageRepository.CreateAndUpdateThreadAsync(message, request.ThreadId, DateTime.UtcNow);
        _logger.LogInformation("Created conversation message {MessageId} in thread {ThreadId}", message.Id, request.ThreadId);

        return message;
    }

    public async Task<ServiceResult<ConversationMessage>> GetLatestConversationMessageAsync(string threadId, string agent, string workflowType, string participantId, string workflowId)
    {
        try
        {
            _logger.LogInformation("Getting latest conversation message for agent {Agent}, workflowType {WorkflowType}, participant {ParticipantId}",
                agent, workflowType, participantId);

            if (string.IsNullOrEmpty(agent) || string.IsNullOrEmpty(workflowType) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields");
                return ServiceResult<ConversationMessage>.BadRequest("Agent, WorkflowType, and ParticipantId are required");
            }
            // Get the latest message from the repository
            var latestMessage = await _conversationChangeListener.GetLatestConversationMessage(
                _tenantContext.TenantId,
                threadId,
                agent,
                workflowType,
                participantId,
                workflowId
            );

            if (latestMessage == null)
            {
                _logger.LogInformation("No existing message found for agent {Agent}, workflowType {WorkflowType}, participant {ParticipantId}",
                    agent, workflowType, participantId);
                return ServiceResult<ConversationMessage>.NotFound("No messages found");
            }

            return ServiceResult<ConversationMessage>.Success(latestMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest conversation message for agent {Agent}, workflowType {WorkflowType}, participant {ParticipantId}",
                agent, workflowType, participantId);
            throw;
        }
    }

}