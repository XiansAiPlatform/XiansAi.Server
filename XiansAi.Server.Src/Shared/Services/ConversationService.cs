using Shared.Auth;
using MongoDB.Bson;
using Temporalio.Exceptions;
using Shared.Repositories;
using Shared.Utils.Services;
using System.Text.Json;

namespace Shared.Services;


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
    public required string Content { get; set; }

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

    /// <summary>
    /// Gets or sets the participant ID of the agent that handed over the message.
    /// Optional.
    /// </summary>
    public string? HandedOverBy { get; set; }

    /// <summary>
    /// Gets or sets the participant ID of the agent that received the message.
    /// Optional.
    /// </summary>
    public string? HandedOverTo { get; set; }
}

public class OutboundMessageRequest
{
    public required string[] WorkflowIds { get; set; }
    public required string ParticipantId { get; set; }
    public required string Content { get; set; }
    public object? Metadata { get; set; }
    public string? HandedOverTo { get; set; }
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
    Task<ServiceResult<MessageProcessingResponse>> ProcessInboundMessage(InboundMessageRequest request);

    /// <summary>
    /// Processes an outbound message, stores it in the database, and signals the workflow.
    /// </summary>
    /// <param name="request">The outbound message request.</param>
    /// <returns>A result object indicating success or failure.</returns>
    Task<ServiceResult<MessageProcessingResponse>> ProcessOutboundMessage(OutboundMessageRequest request);
    
    /// <summary>
    /// Gets conversation message history for a specific workflow with pagination.
    /// </summary>
    /// <param name="workflowId">The conversation workflow ID.</param>
    /// <param name="participantId">The conversation participant ID.</param>
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
    public async Task<ServiceResult<MessageProcessingResponse>> ProcessOutboundMessage(OutboundMessageRequest request)
    {
       _logger.LogInformation("Processing outbound message for workflows {WorkflowIds} from participant {ParticipantId}",
            request.WorkflowIds, request.ParticipantId);

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

            List<MessageLogEvent> logs = new List<MessageLogEvent>();

            if (string.IsNullOrEmpty(request.HandedOverTo))
            {
                // This is a message from the agent to the participant, so we need to notify the webhooks
                logs = await NotifyWebhooksAsync(request);
            }
            else if (request.WorkflowIds.Length > 1)
            {
                // This is a message handover to another agent, so we need to signal the agent
                _logger.LogInformation("Handed over to {HandedOverTo}", request.HandedOverTo);

                var signalRequest = new InboundMessageRequest
                {
                    // Set workflow id to the id of the handed over to agent
                    WorkflowId = request.WorkflowIds[0],
                    ParticipantId = request.ParticipantId,
                    Content = request.Content,
                    Metadata = request.Metadata,
                    HandedOverBy = request.WorkflowIds[0],
                    HandedOverTo = request.HandedOverTo
                };
                // This is a message handover to another agent, so we need to signal the agent
                var signalResponse = await PrepareAndSendSignal(signalRequest, tenantId, userId);
                logs = signalResponse.Logs ?? new List<MessageLogEvent>();
                
            } else {
                // This is a response from a HandedOverTo agent, so we should notify the webhooks
                logs = await NotifyWebhooksAsync(request);
            }

            // Get or create thread

            // if this is a handed over message response, there can be more than one workflow ids

            List<string> messageIds = new List<string>();

            var threadId = await GetOrCreateThreadAsync(tenantId, userId, request.WorkflowIds[0], request.ParticipantId);

            foreach (var workflowId in request.WorkflowIds) {
                // Prepare and send message
                var message = await CreateAndSaveMessageAsync(
                    new ConversationMessage
                    {
                        ThreadId = threadId,
                        ParticipantId = request.ParticipantId,
                        TenantId = tenantId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        CreatedBy = userId,
                        Direction = MessageDirection.Outgoing,
                        Content = request.Content,
                        Metadata = request.Metadata,
                        WorkflowId = workflowId,
                        HandedOverTo = request.HandedOverTo,
                        Logs = new List<MessageLogEvent>
                        {
                            new MessageLogEvent
                            {
                                Event = "Created",
                                Timestamp = DateTime.UtcNow,
                                Details = "Message created"
                            }
                        }.Concat(logs).ToList()
                    }
                );

                messageIds.Add(message.Id);

                _logger.LogInformation("Successfully processed outbound message {MessageId}",
                    message.Id);
            }


            return ServiceResult<MessageProcessingResponse>.Success(new MessageProcessingResponse
            {
                MessageIds = messageIds.ToArray()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing outbound message for workflows {WorkflowIds} from participant {ParticipantId}",
                request.WorkflowIds, request.ParticipantId);

            throw;
        }
    }

    private async Task<List<MessageLogEvent>> NotifyWebhooksAsync(OutboundMessageRequest request)
    {
        // TODO: Implement webhook notification
        return new List<MessageLogEvent>();
    }

    private string? ValidateRequest(OutboundMessageRequest request)
    {
        if (request == null)
        {
            return "Request cannot be null";
        }

        if (request.WorkflowIds == null || request.WorkflowIds.Length == 0)
        {
            return "WorkflowIds is required";
        }

        if (string.IsNullOrEmpty(request.ParticipantId) )
        {
            return "ParticipantId is required";
        }

        return null;
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


            // Prepare and send message
            var message = await PrepareAndSendSignal(request, tenantId, userId);

            _logger.LogInformation("Successfully processed inbound message {MessageId}",
                message.Id);

            return ServiceResult<MessageProcessingResponse>.Success(new MessageProcessingResponse
            {
                MessageIds = new string[] { message.Id }
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
    private async Task<ConversationMessage> PrepareAndSendSignal(
        InboundMessageRequest request, 
        string tenantId, 
        string userId)
    {
        // Create message
        var message = new ConversationMessage
        {
            ThreadId = string.Empty, //this is not used by the signal clients
            TenantId = tenantId,
            ParticipantId = request.ParticipantId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            Direction = MessageDirection.Incoming,
            Content = request.Content,
            Metadata = request.Metadata,
            WorkflowId = request.WorkflowId,
            HandedOverTo = request.HandedOverTo,
            HandedOverBy = request.HandedOverBy
        };

        _logger.LogInformation("Preparing and sending message {MessageId} to workflow {WorkflowId}",
            message.Id, request.WorkflowId);

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
        message = await CreateAndSaveMessageAsync(message);
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
        _logger.LogInformation("Created conversation message {MessageId}",
            message.Id);

        // Update thread's UpdatedAt timestamp
        await _threadRepository.UpdateLastActivityAsync(message.ThreadId, DateTime.UtcNow);
        _logger.LogDebug("Updated last activity timestamp for thread {WorkflowId}", message.WorkflowId);

        return message;
    }

    /// <summary>
    /// Signals the workflow with the inbound message.
    /// </summary>
    /// <param name="message">The conversation message.</param>
    private async Task SignalWorkflowAsync(ConversationMessage message)
    {
        var signalRequest = new WorkflowSignalRequest
        {
            // If the message is handed over to another agent, use the workflow id of the agent that handed over the message
            WorkflowId = message.HandedOverTo ?? message.WorkflowId,
            SignalName = SIGNAL_NAME_INBOUND_MESSAGE,
            Payload =  new {
                message.Content,
                message.Metadata,
                message.CreatedAt,
                message.CreatedBy,
                message.ParticipantId,
                message.HandedOverBy
            }
        };

        await _workflowSignalService.HandleSignalWorkflow(signalRequest);
        _logger.LogInformation("Sent inbound message signal to workflow {WorkflowId}", message.WorkflowId);
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
            
            // Get tenant ID from the context
            string tenantId = _tenantContext.TenantId;
            string userId = _tenantContext.LoggedInUser ?? throw new InvalidOperationException("User not found");
            if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(participantId))
            {
                _logger.LogWarning("Invalid request: missing required fields");
                return ServiceResult<List<ConversationMessage>>.BadRequest("WorkflowId and ParticipantId are required");
            }
            
            if (page < 1)
            {
                _logger.LogWarning("Invalid request: page must be greater than 0");
                return ServiceResult<List<ConversationMessage>>.BadRequest("Page must be greater than 0");
            }
            
            if (pageSize < 1)
            {
                _logger.LogWarning("Invalid request: pageSize must be greater than 0");
                return ServiceResult<List<ConversationMessage>>.BadRequest("PageSize must be greater than 0");
            }

            // Get or create thread
            var threadId = await GetOrCreateThreadAsync(tenantId, userId, workflowId, participantId);

            _logger.LogInformation("Getting messages for thread {ThreadId}", threadId);
            
            // Get messages
            var messages = await _messageRepository.GetByThreadIdAsync(tenantId, threadId, page, pageSize);
            
            _logger.LogInformation("Found {Count} messages for thread {ThreadId}", messages.Count, threadId);
            return ServiceResult<List<ConversationMessage>>.Success(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting message history for workflow {WorkflowId}, participant {ParticipantId}", workflowId, participantId);
            throw;
        }
    }
}