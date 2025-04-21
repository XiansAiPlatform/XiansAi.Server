using Shared.Auth;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Shared.Services;

public enum DeliveryMode
{
    FailIfNotRunning,
    QueueIfNotRunning
}

/// <summary>
/// Request model for inbound conversation messages.
/// </summary>
public class InboundSignalRequest
{
    public required string ParticipantId { get; set; }
    public required string Content { get; set; }
    public required string WorkflowId { get; set; }
    public object? Metadata { get; set; }
    public string? ParentWorkflowId { get; set; }
}

public class OutboundSendRequest
{
    public required string WorkflowId { get; set; }
    public required string ParticipantId { get; set; }
    public required string Content { get; set; }
    public object? Metadata { get; set; }
}

public class OutboundHandoverRequest
{
    public required string WorkflowId { get; set; }
    public required string ParticipantId { get; set; }
    public required string Content { get; set; }
    public object? Metadata { get; set; }
    public required string ParentWorkflowId { get; set; }

    // Optional parameters for signaling existing workflow
    public string? ChildWorkflowId { get; set; }

    // Optional parameters for starting a new workflow
    public string? WorkflowTypeToStart { get; set; }
    public string? QueueName { get; set; }
    public string? Agent { get; set; }
    public string? Assignment { get; set; }
}

public class OutboundHandoverResponse
{
    public required string WorkflowId { get; set; }
    public required string ParticipantId { get; set; }
    public required string Content { get; set; }
    public object? Metadata { get; set; }
    public required string ParentWorkflowId { get; set; }
}

/// <summary>
/// Response model for successful message processing
/// </summary>
public class MessageProcessingResponse
{
    public required string[] MessageIds { get; set; }
}

/// <summary>
/// Service interface for handling conversation operations.
/// </summary>
public interface IConversationService
{
    /// <summary>
    /// Processes an inbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The inbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    Task<ServiceResult<MessageProcessingResponse>> ProcessInboundMessage(InboundSignalRequest request);

    /// <summary>
    /// Processes an outbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The outbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    Task<ServiceResult<MessageProcessingResponse>> ProcessOutboundMessage(OutboundSendRequest request);

    /// <summary>
    /// Processes an outbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The outbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    Task<ServiceResult<MessageProcessingResponse>> ProcessOutboundHandover(OutboundHandoverRequest request);

    /// <summary>
    /// Processes an outbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The outbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    Task<ServiceResult<MessageProcessingResponse>> ProcessOutboundHandoverResponse(OutboundHandoverResponse request);

    /// <summary>
    /// Gets conversation message history for a specific thread with pagination.
    /// </summary>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="participantId">The participant ID.</param>
    /// <param name="page">The page number (1-based).</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>A list of conversation messages.</returns>
    Task<ServiceResult<List<ConversationMessage>>> GetMessageHistoryAsync(string workflowId, string participantId, int page, int pageSize);
}

/// <summary>
/// Service for managing conversations between agents and participants.
/// </summary>
public class ConversationService : IConversationService
{
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IConversationThreadRepository _threadRepository;
    private readonly IWorkflowSignalService _workflowSignalService;
    private readonly ILogger<ConversationService> _logger;
    private readonly ITenantContext _tenantContext;

    public const string SIGNAL_NAME_INBOUND_MESSAGE = "HandleInboundMessage";

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationService"/> class.
    /// </summary>
    /// <param name="messageRepository">Repository for conversation messages.</param>
    /// <param name="threadRepository">Repository for conversation threads.</param>
    /// <param name="workflowSignalService">Service for signaling workflows.</param>
    /// <param name="logger">Logger for recording operational information.</param>
    /// <param name="tenantContext">Context providing tenant information.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public ConversationService(
        IConversationMessageRepository messageRepository,
        IConversationThreadRepository threadRepository,
        IWorkflowSignalService workflowSignalService,
        ILogger<ConversationService> logger,
        ITenantContext tenantContext)
    {
        _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
        _threadRepository = threadRepository ?? throw new ArgumentNullException(nameof(threadRepository));
        _workflowSignalService = workflowSignalService ?? throw new ArgumentNullException(nameof(workflowSignalService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Processes an outbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The outbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    public async Task<ServiceResult<MessageProcessingResponse>> ProcessOutboundMessage(OutboundSendRequest request)
    {
        _logger.LogInformation("Processing outbound message from workflow {WorkflowId} to participant {ParticipantId}",
             request.WorkflowId, request.ParticipantId);

        try
        {
            var threadId = await GetOrCreateThreadAsync(_tenantContext.TenantId, _tenantContext.LoggedInUser, request.WorkflowId, request.ParticipantId, false);

            var message = await CreateMessageAsync(
                threadId,
                request.ParticipantId,
                MessageDirection.Outgoing,
                request.Content,
                request.Metadata,
                CreateLogEvent("Outbound Message", "Outbound message sent to participant"));

            await NotifyWebhooksAsync(message);

            return ServiceResult<MessageProcessingResponse>.Success(new MessageProcessingResponse
            {
                MessageIds = [message.Id]
            });
        }
        catch (Exception ex)
        {
            LogError(ex, "Error processing outbound message", request.WorkflowId, request.ParticipantId);
            throw;
        }
    }

    /// <summary>
    /// Processes an outbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The outbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    public async Task<ServiceResult<MessageProcessingResponse>> ProcessOutboundHandover(OutboundHandoverRequest request)
    {
        if (request.ChildWorkflowId != null)
        {
            _logger.LogInformation("Processing outbound handover for existing workflow '{WorkflowId}' to child workflow '{ChildWorkflowId}'", 
                request.WorkflowId, request.ChildWorkflowId);
            return await ProcessHandoverExistingWorkflow(request);
        }
        else
        {
            _logger.LogInformation("Processing outbound handover for new workflow type '{WorkflowType}' from workflow '{ParentWorkflowId}'", 
                request.WorkflowTypeToStart, request.WorkflowId);
            return await ProcessHandoverNewWorkflow(request);
        }
    }

    public async Task<ServiceResult<MessageProcessingResponse>> ProcessHandoverNewWorkflow(OutboundHandoverRequest request)
    {
        if (request.WorkflowTypeToStart == null)
        {
            _logger.LogError("Workflow type to start is required when starting a new workflow");
            throw new ArgumentNullException(nameof(request.WorkflowTypeToStart), "Workflow type to start is required when starting a new workflow");
        }

        var agentName = request.Agent ?? request.WorkflowTypeToStart;

        // Generate new workflow id for the new workflow
        var childWorkflowId = NewWorkflowOptions.GenerateNewWorkflowId(null, agentName, request.WorkflowTypeToStart, _tenantContext);
        request.ChildWorkflowId = childWorkflowId;
        _logger.LogDebug("Generated new workflow ID '{ChildWorkflowId}' for workflow type '{WorkflowType}'", 
            childWorkflowId, request.WorkflowTypeToStart);

        // Create outbound and inbound handover messages
        var messageOutbound = await CreateOutboundHandoverMessageAsync(request);
        _logger.LogDebug("Created outbound handover message with ID '{MessageId}' in parent workflow '{ParentWorkflowId}'", 
            messageOutbound.Id, request.ParentWorkflowId);
        
        var messageInbound = await CreateInboundHandoverMessageAsync(request);
        _logger.LogDebug("Created inbound handover message with ID '{MessageId}' in child workflow '{ChildWorkflowId}'", 
            messageInbound.Id, childWorkflowId);

        _logger.LogDebug("Signaling start of workflow '{ChildWorkflowId}' of type '{WorkflowType}'", 
            childWorkflowId, request.WorkflowTypeToStart);
        await SignalWithStartWorkflowAsync(
            childWorkflowId,
            request.Content,
            request.Metadata,
            request.ParticipantId,
            request.ParentWorkflowId!,
            request.WorkflowTypeToStart,
            request.QueueName,
            request.Agent,
            request.Assignment);

        _logger.LogInformation("Successfully started new workflow '{ChildWorkflowId}' of type '{WorkflowType}' with handover from '{ParentWorkflowId}'", 
            childWorkflowId, request.WorkflowTypeToStart, request.ParentWorkflowId);

        // Return the message ids
        return ServiceResult<MessageProcessingResponse>.Success(new MessageProcessingResponse
        {
            MessageIds = [messageOutbound.Id, messageInbound.Id]
        });
    }

    public async Task<ServiceResult<MessageProcessingResponse>> ProcessHandoverExistingWorkflow(OutboundHandoverRequest request)
    {
        _logger.LogInformation("Processing outbound handover from workflow '{ParentWorkflowId}' to existing workflow '{ChildWorkflowId}'",
             request.WorkflowId, request.ChildWorkflowId);

        if (request.ChildWorkflowId == null)
        {
            _logger.LogError("Child workflow id is required when processing outbound handover for existing workflow");
            throw new ArgumentNullException(nameof(request.ChildWorkflowId), "Child workflow id is required when processing outbound handover for existing workflow");
        }

        try
        {
            // Create outbound and inbound handover messages
            var messageOutbound = await CreateOutboundHandoverMessageAsync(request);
            _logger.LogDebug("Created outbound handover message with ID '{MessageId}' in parent workflow '{ParentWorkflowId}'", 
                messageOutbound.Id, request.WorkflowId);
            
            var messageInbound = await CreateInboundHandoverMessageAsync(request);
            _logger.LogDebug("Created inbound handover message with ID '{MessageId}' in child workflow '{ChildWorkflowId}'", 
                messageInbound.Id, request.ChildWorkflowId);

            _logger.LogDebug("Signaling existing workflow '{ChildWorkflowId}' with handover from '{ParentWorkflowId}'", 
                request.ChildWorkflowId, request.WorkflowId);
            await SignalWorkflowAsync(
                request.Content,
                request.Metadata,
                request.ParticipantId,
                request.ChildWorkflowId!,
                request.WorkflowId);

            _logger.LogInformation("Successfully signaled existing workflow '{ChildWorkflowId}' with handover from '{ParentWorkflowId}'", 
                request.ChildWorkflowId, request.WorkflowId);

            // Return the message ids
            return ServiceResult<MessageProcessingResponse>.Success(new MessageProcessingResponse
            {
                MessageIds = [messageOutbound.Id, messageInbound.Id]
            });
        }
        catch (Exception ex)
        {
            LogError(ex, "Error processing outbound handover", request.WorkflowId, childWorkflowId: request.ChildWorkflowId);
            throw;
        }
    }

    /// <summary>
    /// Creates an outbound handover message from parent workflow to child workflow.
    /// </summary>
    /// <param name="request">The handover request containing message details.</param>
    /// <returns>The created conversation message.</returns>
    private async Task<ConversationMessage> CreateOutboundHandoverMessageAsync(OutboundHandoverRequest request)
    {
        // Source thread id
        var threadIdOutbound = await GetOrCreateThreadAsync(_tenantContext.TenantId, _tenantContext.LoggedInUser, request.ParentWorkflowId, request.ChildWorkflowId!, true);

        // Save outgoing message for Handed Over By workflow
        return await CreateMessageAsync(
            threadIdOutbound,
            request.ParticipantId,
            MessageDirection.Outgoing,
            request.Content,
            request.Metadata,
            CreateLogEvent("In Parent Thread - To Child Handover", "Message handed over to another workflow"),
            childWorkflowId: request.ChildWorkflowId);
    }

    /// <summary>
    /// Creates an inbound handover message in child workflow from parent workflow.
    /// </summary>
    /// <param name="request">The handover request containing message details.</param>
    /// <returns>The created conversation message.</returns>
    private async Task<ConversationMessage> CreateInboundHandoverMessageAsync(OutboundHandoverRequest request)
    {
        // Target thread id
        var threadIdInbound = await GetOrCreateThreadAsync(_tenantContext.TenantId, _tenantContext.LoggedInUser, request.ChildWorkflowId!, request.ParentWorkflowId, true);

        // Save incoming message for Handed Over To workflow
        return await CreateMessageAsync(
            threadIdInbound,
            request.ParticipantId,
            MessageDirection.Incoming,
            request.Content,
            request.Metadata,
            CreateLogEvent("In Child Thread - From Parent Handover", "Message handed over from another workflow"),
            parentWorkflowId: request.WorkflowId);
    }

    public async Task<ServiceResult<MessageProcessingResponse>> ProcessOutboundHandoverResponse(OutboundHandoverResponse request)
    {
        _logger.LogInformation("Processing handover response message from child workflow '{ChildWorkflowId}' through parent workflow '{ParentWorkflowId}'",
             request.WorkflowId, request.ParentWorkflowId);
        try
        {
            // Thread between child and parent
            var threadIdHandedOverTo = await GetOrCreateThreadAsync(_tenantContext.TenantId, _tenantContext.LoggedInUser, request.WorkflowId, request.ParentWorkflowId, true);
            _logger.LogDebug("Using thread '{ThreadId}' between child workflow '{ChildWorkflowId}' and parent workflow '{ParentWorkflowId}'", 
                threadIdHandedOverTo, request.WorkflowId, request.ParentWorkflowId);

            // Thread between parent and child
            var threadIdHandedOverBy = await GetOrCreateThreadAsync(_tenantContext.TenantId, _tenantContext.LoggedInUser, request.ParentWorkflowId, request.WorkflowId, true);
            _logger.LogDebug("Using thread '{ThreadId}' between parent workflow '{ParentWorkflowId}' and child workflow '{ChildWorkflowId}'", 
                threadIdHandedOverBy, request.ParentWorkflowId, request.WorkflowId);

            // Thread between parent and participant
            var threadIdHandedOverByParticipant = await GetOrCreateThreadAsync(_tenantContext.TenantId, _tenantContext.LoggedInUser, request.ParentWorkflowId, request.ParticipantId, false);
            _logger.LogDebug("Using thread '{ThreadId}' between parent workflow '{ParentWorkflowId}' and participant '{ParticipantId}'", 
                threadIdHandedOverByParticipant, request.ParentWorkflowId, request.ParticipantId);

            _logger.LogDebug("Creating batch of 3 messages for handover response");
            // Create all three messages in a batch operation
            var messageIds = await CreateMessagesAsync(
                new List<(string threadId, string participantId, MessageDirection direction, string content, object? metadata, MessageLogEvent logEvent, string? childWorkflowId, string? parentWorkflowId)>
                {
                    // Message 1: Outgoing message for HandedOverTo workflow
                    (threadIdHandedOverTo, request.ParticipantId, MessageDirection.Outgoing, request.Content, request.Metadata,
                     CreateLogEvent("Handover Response to Handed Over By", "Message handed over response from Handed Over To workflow"),
                     null, request.ParentWorkflowId),
                    
                    // Message 2: Incoming message for HandedOverBy workflow
                    (threadIdHandedOverBy, request.ParticipantId, MessageDirection.Incoming, request.Content, request.Metadata,
                     CreateLogEvent("Handover Response Passthrough", "Message handed over response from Handed Over To workflow"),
                     null, request.ParentWorkflowId),
                     
                    // Message 3: Outgoing message from HandedOverBy to Participant
                    (threadIdHandedOverByParticipant, request.ParticipantId, MessageDirection.Outgoing, request.Content, request.Metadata,
                     CreateLogEvent("Handover Response Delivered to Participant", "Message handed over response passthrough from Handed Over By workflow"),
                     request.WorkflowId, null)
                });

            _logger.LogInformation("Successfully processed handover response: created 3 messages with IDs [{MessageIds}]", 
                string.Join(", ", messageIds));

            // Return the message ids
            return ServiceResult<MessageProcessingResponse>.Success(new MessageProcessingResponse
            {
                MessageIds = messageIds.ToArray()
            });
        }
        catch (Exception ex)
        {
            LogError(ex, "Error processing outbound handover response", request.WorkflowId, parentWorkflowId: request.ParentWorkflowId);
            throw;
        }
    }

    private async Task<List<MessageLogEvent>> NotifyWebhooksAsync(ConversationMessage message)
    {
        // TODO: Implement webhook notification
        _logger.LogDebug("Webhook notification not implemented yet for message '{MessageId}'", message.Id);
        return new List<MessageLogEvent>();
    }

    /// <summary>
    /// Processes an inbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The inbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    public async Task<ServiceResult<MessageProcessingResponse>> ProcessInboundMessage(InboundSignalRequest request)
    {
        _logger.LogInformation("Processing inbound message for agent {AgentId} from participant {ParticipantId}",
            request.WorkflowId, request.ParticipantId);

        try
        {
            var threadId = await GetOrCreateThreadAsync(_tenantContext.TenantId, _tenantContext.LoggedInUser, request.WorkflowId, request.ParticipantId, false);

            // Create and save the inbound message
            var messageInbound = await CreateMessageAsync(
                threadId,
                request.ParticipantId,
                MessageDirection.Incoming,
                request.Content,
                request.Metadata,
                CreateLogEvent("Inbound Message", "Message received from participant"),
                parentWorkflowId: request.ParentWorkflowId);

            // Signal the workflow
            await SignalWorkflowAsync(
                request.Content,
                request.Metadata,
                request.ParticipantId,
                request.WorkflowId,
                request.ParentWorkflowId);

            _logger.LogInformation("Successfully processed inbound message {MessageId}", messageInbound.Id);

            return ServiceResult<MessageProcessingResponse>.Success(new MessageProcessingResponse
            {
                MessageIds = [messageInbound.Id]
            });
        }
        catch (WorkflowNotFoundException ex)
        {
            _logger.LogWarning(ex, "Workflow not found while processing message");
            return ServiceResult<MessageProcessingResponse>.NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            LogError(ex, "Error processing inbound message", request.WorkflowId, request.ParticipantId);
            throw;
        }
    }

    private void LogError(Exception ex, string message, string workflowId, string? participantId = null, string? childWorkflowId = null, string? parentWorkflowId = null)
    {
        if (participantId != null && childWorkflowId != null)
        {
            _logger.LogError(ex, "{Message} for workflow {WorkflowId} to workflow {ChildWorkflowId} for participant {ParticipantId}",
                message, workflowId, childWorkflowId, participantId);
        }
        else if (participantId != null && parentWorkflowId != null)
        {
            _logger.LogError(ex, "{Message} for workflow {WorkflowId} from workflow {ParentWorkflowId} for participant {ParticipantId}",
                message, workflowId, parentWorkflowId, participantId);
        }
        else if (participantId != null)
        {
            _logger.LogError(ex, "{Message} for workflow {WorkflowId} to workflow {ChildWorkflowId}",
                message, workflowId, childWorkflowId);
        }
        else if (parentWorkflowId != null)
        {
            _logger.LogError(ex, "{Message} for workflow {WorkflowId} from workflow {ParentWorkflowId}",
                message, workflowId, parentWorkflowId);
        }
        else if (participantId != null)
        {
            _logger.LogError(ex, "{Message} for workflow {WorkflowId} from participant {ParticipantId}",
                message, workflowId, participantId);
        }
        else
        {
            _logger.LogError(ex, "{Message} for workflow {WorkflowId}",
                message, workflowId);
        }
    }

    /// <summary>
    /// Creates a message log event.
    /// </summary>
    private MessageLogEvent CreateLogEvent(string eventName, string details)
    {
        return new MessageLogEvent
        {
            Event = eventName,
            Timestamp = DateTime.UtcNow,
            Details = details
        };
    }

    /// <summary>
    /// Exception thrown when a workflow is not found.
    /// </summary>
    public class WorkflowNotFoundException : Exception
    {
        public WorkflowNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Gets an existing thread or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="participantId">The participant ID.</param>
    /// <param name="isInternalThread">Whether the thread is internal.</param>
    /// <returns>The thread ID.</returns>
    private async Task<string> GetOrCreateThreadAsync(string tenantId, string userId, string workflowId, string participantId, bool isInternalThread)
    {
        _logger.LogDebug("Looking for thread between '{WorkflowId}' and '{ParticipantId}'", workflowId, participantId);
        var thread = await _threadRepository.GetByCompositeKeyAsync(
            tenantId, workflowId, participantId);

        if (thread == null)
        {
            // Create new thread
            var newThread = new ConversationThread
            {
                TenantId = tenantId,
                WorkflowId = workflowId,
                ParticipantId = participantId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
                Status = ConversationThreadStatus.Active,
                IsInternalThread = isInternalThread
            };

            var threadId = await _threadRepository.CreateAsync(newThread);
            _logger.LogInformation("Created new conversation thread '{ThreadId}' for workflow '{WorkflowId}' and participant '{ParticipantId}'",
                threadId, workflowId, participantId);

            return threadId;
        }
        else
        {
            var threadId = thread.Id;

            // Update thread status if it was archived/closed
            if (thread.Status != ConversationThreadStatus.Active)
            {
                await _threadRepository.UpdateStatusAsync(threadId, ConversationThreadStatus.Active);
                _logger.LogInformation("Reactivated conversation thread '{ThreadId}' for workflow '{WorkflowId}' and participant '{ParticipantId}'",
                    threadId, workflowId, participantId);
            }
            else
            {
                _logger.LogDebug("Using existing active thread '{ThreadId}' for workflow '{WorkflowId}' and participant '{ParticipantId}'",
                    threadId, workflowId, participantId);
            }

            return threadId;
        }
    }

    /// <summary>
    /// Creates and saves a conversation message with the specified parameters.
    /// </summary>
    private async Task<ConversationMessage> CreateMessageAsync(
        string threadId,
        string participantId,
        MessageDirection direction,
        string content,
        object? metadata,
        MessageLogEvent logEvent,
        string? childWorkflowId = null,
        string? parentWorkflowId = null)
    {
        var message = new ConversationMessage
        {
            ThreadId = threadId,
            ParticipantId = participantId,
            TenantId = _tenantContext.TenantId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            Direction = direction,
            Content = content,
            Metadata = metadata,
            ChildWorkflowId = childWorkflowId,
            ParentWorkflowId = parentWorkflowId,
            Logs = new List<MessageLogEvent> { logEvent }
        };

        // Save message to database and update thread in a single transaction
        message.Id = await _messageRepository.CreateAndUpdateThreadAsync(message, threadId, DateTime.UtcNow);
        _logger.LogInformation("Created conversation message {MessageId} in thread {ThreadId}", message.Id, threadId);

        return message;
    }

    /// <summary>
    /// Creates and saves multiple conversation messages in a single batch operation.
    /// </summary>
    private async Task<List<string>> CreateMessagesAsync(
        List<(string threadId, string participantId, MessageDirection direction, string content, object? metadata, MessageLogEvent logEvent, string? childWorkflowId, string? parentWorkflowId)> messageParams)
    {
        var messages = new List<ConversationMessage>();
        var threadTimestamps = new Dictionary<string, DateTime>();
        var timestamp = DateTime.UtcNow;

        foreach (var (threadId, participantId, direction, content, metadata, logEvent, childWorkflowId, parentWorkflowId) in messageParams)
        {
            messages.Add(new ConversationMessage
            {
                ThreadId = threadId,
                ParticipantId = participantId,
                TenantId = _tenantContext.TenantId,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
                CreatedBy = _tenantContext.LoggedInUser,
                Direction = direction,
                Content = content,
                Metadata = metadata,
                ChildWorkflowId = childWorkflowId,
                ParentWorkflowId = parentWorkflowId,
                Logs = new List<MessageLogEvent> { logEvent }
            });

            // Add thread ID to the update collection with the same timestamp
            threadTimestamps[threadId] = timestamp;
        }

        // Save all messages and update all threads in a single transaction
        var messageIds = await _messageRepository.CreateManyAndUpdateThreadsAsync(messages, threadTimestamps);

        // Log created messages
        for (int i = 0; i < messages.Count; i++)
        {
            _logger.LogInformation("Created conversation message {MessageId} in thread {ThreadId}",
                messageIds[i], messages[i].ThreadId);
        }

        return messageIds;
    }

    /// <summary>
    /// Signals the workflow with the inbound message.
    /// </summary>
    private async Task SignalWithStartWorkflowAsync(string proposedWorkflowId, string Content, object? Metadata, string ParticipantId,
        string ParentWorkflowId,
        string WorkflowType,
        string? QueueName = null,
        string? Agent = null,
        string? Assignment = null)
    {
        _logger.LogDebug("Preparing to signal and start workflow '{WorkflowId}' of type '{WorkflowType}' with participant '{ParticipantId}' from parent '{ParentWorkflowId}'",
            proposedWorkflowId, WorkflowType, ParticipantId, ParentWorkflowId);
        
        var request = new WorkflowSignalWithStartRequest
        {
            ProposedWorkflowId = proposedWorkflowId,
            WorkflowType = WorkflowType ?? throw new ArgumentNullException(nameof(WorkflowType), "WorkflowType is required when signaling with start"),
            SignalName = SIGNAL_NAME_INBOUND_MESSAGE,
            Payload = new
            {
                Content,
                Metadata,
                ParticipantId,
                ParentWorkflowId
            },
            QueueName = QueueName,
            Agent = Agent,
            Assignment = Assignment
        };
        
        await _workflowSignalService.SignalWithStartWorkflow(request);

        _logger.LogInformation("Sent inbound message signal to start new workflow '{WorkflowId}' of type '{WorkflowType}'", 
            proposedWorkflowId, WorkflowType);
    }

    /// <summary>
    /// Signals the workflow with the inbound message.
    /// </summary>
    private async Task SignalWorkflowAsync(string Content, object? Metadata, string ParticipantId, string WorkflowId, string? ParentWorkflowId = null)
    {
        _logger.LogDebug("Preparing to signal workflow '{WorkflowId}' with participant '{ParticipantId}'{ParentWorkflowInfo}",
            WorkflowId, ParticipantId, ParentWorkflowId != null ? $" from parent '{ParentWorkflowId}'" : "");
        
        var request = new WorkflowSignalRequest
        {
            WorkflowId = WorkflowId,
            SignalName = SIGNAL_NAME_INBOUND_MESSAGE,
            Payload = new
            {
                Content,
                Metadata,
                ParticipantId,
                ParentWorkflowId,
            }
        };
        
        await _workflowSignalService.SignalWorkflow(request);

        _logger.LogInformation("Sent inbound message signal to workflow '{WorkflowId}'{ParentWorkflowInfo}", 
            WorkflowId, ParentWorkflowId != null ? $" from parent '{ParentWorkflowId}'" : "");
    }

    /// <summary>
    /// Gets conversation message history for a specific thread with pagination.
    /// </summary>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="participantId">The participant ID.</param>
    /// <param name="page">The page number (1-based).</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>A list of conversation messages.</returns>
    public async Task<ServiceResult<List<ConversationMessage>>> GetMessageHistoryAsync(string workflowId, string participantId, int page, int pageSize)
    {
        try
        {
            _logger.LogInformation("Getting message history for workflow {WorkflowId}, participant {ParticipantId}, page {Page}, pageSize {PageSize}",
                workflowId, participantId, page, pageSize);

            if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields");
                return ServiceResult<List<ConversationMessage>>.BadRequest("WorkflowId and ParticipantId are required");
            }

            if (page < 1 || pageSize < 1)
            {
                _logger.LogWarning("Invalid request: page and pageSize must be greater than 0");
                return ServiceResult<List<ConversationMessage>>.BadRequest("Page and PageSize must be greater than 0");
            }

            // Get messages directly by workflow and participant IDs
            var messages = await _messageRepository.GetByWorkflowAndParticipantAsync(_tenantContext.TenantId, workflowId, participantId, page, pageSize);

            _logger.LogInformation("Found {Count} messages for workflow {WorkflowId} and participant {ParticipantId}",
                messages.Count, workflowId, participantId);

            return ServiceResult<List<ConversationMessage>>.Success(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting message history for workflow {WorkflowId}, participant {ParticipantId}", workflowId, participantId);
            throw;
        }
    }
}