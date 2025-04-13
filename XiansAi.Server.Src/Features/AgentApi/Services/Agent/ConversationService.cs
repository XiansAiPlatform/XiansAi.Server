using XiansAi.Server.Src.Features.AgentApi.Repositories;
using Shared.Auth;
using MongoDB.Bson;
using Temporalio.Exceptions;


namespace Features.AgentApi.Services.Agent;


public enum DeliveryMode
{
    FailIfNotRunning,
    QueueIfNotRunning
}

/// <summary>
/// Request model for inbound conversation messages.
/// </summary>
public class InboundMessageRequest
{
    /// <summary>
    /// Gets or sets the channel independent unique identifier of the participant. For example the user id in the system.
    /// Required.
    /// </summary>
    public required string ParticipantId { get; set; }

    /// <summary>
    /// Gets or sets the message content.
    /// Required.
    /// </summary>
    public required object Content { get; set; }


    /// <summary>
    /// Gets or sets a unique identifier of the participant in the channel. For example, the user ID in Slack.
    /// Required.
    /// </summary>
    public required string ParticipantChannelId { get; set; }

    /// <summary>
    /// Gets or sets the workflow ID.
    /// Required.
    /// </summary>
    public required string WorkflowId { get; set; }
    /// <summary>
    /// Gets or sets additional metadata for the message.
    /// Optional.
    /// </summary>
    public object? Metadata { get; set; }


}

/// <summary>
/// Response model for successful message processing
/// </summary>
public class MessageProcessingResponse
{
    public required string MessageId { get; set; }
    public required string ThreadId { get; set; }
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
    Task<ServiceResult<MessageProcessingResponse>> ProcessInboundMessage(InboundMessageRequest request);
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
    /// Processes an inbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The inbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    public async Task<ServiceResult<MessageProcessingResponse>> ProcessInboundMessage(InboundMessageRequest request)
    {
        _logger.LogInformation("Processing inbound message for agent {AgentId} from participant {ParticipantId}",
            request.WorkflowId, request.ParticipantId);

        try
        {
            // Validate request
            var validationResult = ValidateRequest(request);
            if (validationResult != null)
            {
                return ServiceResult<MessageProcessingResponse>.BadRequest(validationResult);
            }

            // Get tenant ID and user ID
            string tenantId = _tenantContext.TenantId;
            string userId = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("User not found");

            // Get or create thread
            string threadId = await GetOrCreateThreadAsync(tenantId, userId, request.WorkflowId, request.ParticipantId);

            // Prepare and send message
            var message = await PrepareAndSendMessage(request, tenantId, userId, threadId);

            _logger.LogInformation("Successfully processed inbound message {MessageId} for thread {ThreadId}",
                message.Id, threadId);

            return ServiceResult<MessageProcessingResponse>.Success(new MessageProcessingResponse
            {
                MessageId = message.Id,
                ThreadId = threadId
            });
        }
        catch (WorkflowNotFoundException ex)
        {
            _logger.LogWarning(ex, "Workflow not found while processing message");
            return ServiceResult<MessageProcessingResponse>.NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing inbound message for agent {AgentId} from participant {ParticipantId}",
                request.WorkflowId, request.ParticipantId);

            throw;
        }
    }

    /// <summary>
    /// Prepares the message and sends it to the workflow.
    /// </summary>
    private async Task<ConversationMessage> PrepareAndSendMessage(
        InboundMessageRequest request, 
        string tenantId, 
        string userId, 
        string threadId)
    {
        // Create message content
        var messageContent = ConvertToMessageContent(request.Content);
        
        // Create message metadata if provided
        var metadata = request.Metadata != null
            ? ConvertToMessageContent(request.Metadata)
            : null;

        // Create message
        var message = new ConversationMessage
        {
            TenantId = tenantId,
            ThreadId = threadId,
            ParticipantChannelId = request.ParticipantChannelId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            Direction = MessageDirection.Inbound,
            Content = messageContent,
            Metadata = metadata,
            WorkflowId = request.WorkflowId
        };

        try
        {
            // Signal the workflow
            await SignalWorkflowAsync(message);
            message.Status = MessageStatus.DeliveredToWorkflow;
        }
        catch (RpcException ex)
        {
            if (ex.Code == RpcException.StatusCode.NotFound)
            {
                var errorMessage = $"Workflow with id {request.WorkflowId} not found";
                message.Status = MessageStatus.FailedToDeliverToWorkflow;
                message.Logs = CreateErrorLog("ErrorDeliveringToWorkflow", errorMessage);
                
                message = await CreateAndSaveMessageAsync(message);
                _logger.LogError(ex, $"Workflow {request.WorkflowId} not found");
                
                throw new WorkflowNotFoundException(errorMessage, ex);
            }
            
            message.Status = MessageStatus.FailedToDeliverToWorkflow;
            message.Logs = CreateErrorLog("ErrorDeliveringToWorkflow", ex.Message);
            
            await CreateAndSaveMessageAsync(message);
            _logger.LogError(ex, $"Error signaling workflow for message {message.Id}");
            throw;
        }

        // Save the message
        message.Status = MessageStatus.DeliveredToWorkflow;
        message =await CreateAndSaveMessageAsync(message);
        return message;
    }

    /// <summary>
    /// Creates an error log list for a message.
    /// </summary>
    private List<MessageLogEvent> CreateErrorLog(string eventName, string details)
    {
        return new List<MessageLogEvent>
        {
            new MessageLogEvent
            {
                Timestamp = DateTime.UtcNow,
                Event = eventName,
                Details = details
            }
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
    /// Validates the inbound message request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>An error message if validation fails, null otherwise.</returns>
    private string? ValidateRequest(InboundMessageRequest request)
    {
        if (request == null)
        {
            _logger.LogWarning("Received null inbound message request");
            return "Request cannot be null";
        }

        if (string.IsNullOrEmpty(request.WorkflowId))
        {
            _logger.LogWarning("Invalid inbound message request: missing required fields");
            return "WorkflowId is required";
        }

        if (string.IsNullOrEmpty(request.ParticipantId))
        {
            _logger.LogWarning("Invalid inbound message request: missing required fields");
            return "ParticipantId is required";
        }

        if (request.Content == null)
        {
            _logger.LogWarning("Invalid inbound message request: missing required fields");
            return "Content is required";
        }

        return null;
    }

    /// <summary>
    /// Gets an existing thread or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="participantId">The participant ID.</param>
    /// <returns>The thread ID.</returns>
    private async Task<string> GetOrCreateThreadAsync(string tenantId, string userId, string workflowId, string participantId)
    {
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
            };

            var threadId = await _threadRepository.CreateAsync(newThread);
            _logger.LogInformation("Created new conversation thread {ThreadId} for workflow {WorkflowId} and participant {ParticipantId}",
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
                _logger.LogInformation("Reactivated conversation thread {ThreadId} for workflow {WorkflowId} and participant {ParticipantId}",
                    threadId, workflowId, participantId);
            }

            return threadId;
        }
    }

    /// <summary>
    /// Creates and saves a conversation message.
    /// </summary>
    /// <param name="message">The conversation message to create and save.</param>
    /// <returns>The created message.</returns>
    private async Task<ConversationMessage> CreateAndSaveMessageAsync(ConversationMessage message)
    {
        // Save message to database
        message.Id = await _messageRepository.CreateAsync(message);
        _logger.LogInformation("Created conversation message {MessageId} in thread {ThreadId}",
            message.Id, message.ThreadId);

        // Update thread's UpdatedAt timestamp
        await _threadRepository.UpdateLastActivityAsync(message.ThreadId, DateTime.UtcNow);
        _logger.LogDebug("Updated last activity timestamp for thread {ThreadId}", message.ThreadId);

        return message;
    }

    /// <summary>
    /// Converts an object to BsonDocument for message content or metadata.
    /// </summary>
    /// <param name="content">The content to convert.</param>
    /// <returns>The converted BsonDocument.</returns>
    private BsonDocument ConvertToMessageContent(object content)
    {
        return content is BsonDocument bsonContent
            ? bsonContent
            : BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(content));
    }

    /// <summary>
    /// Signals the workflow with the inbound message.
    /// </summary>
    /// <param name="message">The conversation message.</param>
    private async Task SignalWorkflowAsync(ConversationMessage message)
    {
        var signalRequest = new WorkflowSignalRequest
        {
            WorkflowId = message.WorkflowId,
            SignalName = SIGNAL_NAME_INBOUND_MESSAGE,
            Payload = message
        };

        await _workflowSignalService.HandleSignalWorkflow(signalRequest);
        _logger.LogInformation("Sent inbound message signal to workflow {WorkflowId}", message.WorkflowId);
    }
}