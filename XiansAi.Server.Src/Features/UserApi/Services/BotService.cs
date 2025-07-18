using Shared.Auth;
using Shared.Utils.Services;
using Features.UserApi.Repositories;
using Shared.Repositories;
using Shared.Utils.Temporal;
using Temporalio.Client;
using Shared.Utils;

namespace Features.UserApi.Services;

/// <summary>
/// Optimized models for bot operations
/// </summary>
public class BotRequest
{
    public string? RequestId { get; set; }
    public required string ParticipantId { get; set; }
    public string? WorkflowId { get; set; }
    public string? WorkflowType { get; set; }
    public string? Scope { get; set; }
    public string? Hint { get; set; }
    public object? Data { get; set; }
    public string? Text { get; set; }
    public string? ThreadId { get; set; }
    public string? Authorization { get; set; }
}

public class BotResponse
{
    public string? Text { get; set; }
    public object? Data { get; set; }
    public string? RequestId { get; set; }
    public string? Scope { get; set; }
    public string? ParticipantId { get; set; }
    public string? WorkflowId { get; set; }
    public string? WorkflowType { get; set; }
    public required string Agent { get; set; }
    public string? Authorization { get; set; }
    public string? ThreadId { get; set; }
    public bool IsComplete { get; set; } = true;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class BotHistoryResponse
{
    public List<ConversationMessage> Messages { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasNext { get; set; }
    public bool HasPrevious { get; set; }
}

/// <summary>
/// High-performance bot service interface
/// </summary>
public interface IBotService
{
    Task<ServiceResult<BotResponse>> ProcessBotRequestAsync(BotRequest request);
    Task<ServiceResult<BotHistoryResponse>> GetBotHistoryAsync(string workflowId, string participantId, int page, int pageSize, string? scope = null);
}

/// <summary>
/// Optimized bot service with maximum performance and minimal complexity
/// Key optimizations:
/// - Single repository for all operations
/// - Atomic database transactions
/// - Minimal object allocation
/// - Optimized query patterns
/// - Reduced logging overhead
/// </summary>
public class BotService : IBotService
{
    private readonly ILogger<BotService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IConversationRepository _conversationRepository;
    private readonly ITemporalClientService _temporalClientService;


    public BotService(
        ILogger<BotService> logger,
        ITenantContext tenantContext,
        IConversationRepository conversationRepository,
        ITemporalClientService temporalClientService)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _conversationRepository = conversationRepository;
        _temporalClientService = temporalClientService;
    }

    public async Task<ServiceResult<BotResponse>> ProcessBotRequestAsync(BotRequest request)
    {
        try
        {
            // Fast validation with minimal overhead
            var validationResult = ValidateRequest(request);
            if (!validationResult.IsSuccess)
                return ServiceResult<BotResponse>.BadRequest(validationResult.ErrorMessage!);

            // Ensure we have workflow ID and Type
            request.WorkflowId = EnsureWorkflowId(request);
            request.WorkflowType = request.WorkflowType ?? ExtractWorkflowTypeFromId(request.WorkflowId);

            // Get or create thread in single operation
            ConversationThreadInfo? threadInfo;
            if (string.IsNullOrEmpty(request.ThreadId))
            {
                threadInfo = await _conversationRepository.CreateOrGetThreadAsync(
                    request.WorkflowId, 
                    request.ParticipantId);
                request.ThreadId = threadInfo.Id;
            }
            else
            {
                threadInfo = await _conversationRepository.GetThreadInfoAsync(
                    request.WorkflowId, 
                    request.ParticipantId);
                
                if (threadInfo == null)
                    return ServiceResult<BotResponse>.BadRequest("Thread not found");
            }

            // Save incoming message atomically
            await SaveRequestMessageAsync(request);

            // Process bot interaction (currently synchronous for performance)
            var botResponse = await ProcessSynchronousBotAsync(request, threadInfo);

            // Save response message atomically
            await SaveResponseMessageAsync(botResponse);

            return ServiceResult<BotResponse>.Success(botResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing bot request for participant {ParticipantId}", request.ParticipantId);
            return ServiceResult<BotResponse>.InternalServerError($"Internal server error: {ex.Message}");
        }
    }


    public async Task<ServiceResult<BotHistoryResponse>> GetBotHistoryAsync(string workflowId, string participantId, int page, int pageSize, string? scope = null)
    {
        try
        {
            // Fast validation
            if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(participantId))
                return ServiceResult<BotHistoryResponse>.BadRequest("WorkflowId and ParticipantId are required");

            if (page < 1 || pageSize < 1)
                return ServiceResult<BotHistoryResponse>.BadRequest("Page and PageSize must be greater than 0");

            // Ensure workflowId has tenant prefix
            var fullWorkflowId = workflowId.StartsWith(_tenantContext.TenantId + ":") 
                ? workflowId 
                : $"{_tenantContext.TenantId}:{workflowId}";

            // Single optimized query - get one extra message to check if there's a next page
            var messages = await _conversationRepository.GetMessagesAsync(
                fullWorkflowId, 
                participantId, 
                page, 
                pageSize + 1, // Get one extra to check for next page
                scope);

            // Determine pagination metadata
            var hasNext = messages.Count > pageSize;
            if (hasNext)
            {
                messages = messages.Take(pageSize).ToList(); // Remove the extra message
            }

            var hasPrevious = page > 1;

            var response = new BotHistoryResponse
            {
                Messages = messages,
                Page = page,
                PageSize = pageSize,
                HasNext = hasNext,
                HasPrevious = hasPrevious
            };

            return ServiceResult<BotHistoryResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bot history for workflowId {WorkflowId}, participant {ParticipantId}", workflowId, participantId);
            throw;
        }
    }

    #region Private Optimized Methods

    private ServiceResult<bool> ValidateRequest(BotRequest request)
    {
        if (string.IsNullOrEmpty(request.ParticipantId))
            return ServiceResult<bool>.BadRequest("ParticipantId is required");

        return ServiceResult<bool>.Success(true);
    }

    private string EnsureWorkflowId(BotRequest request)
    {
        if (!string.IsNullOrEmpty(request.WorkflowId))
        {
            // Validate workflow ID format
            if (!request.WorkflowId.StartsWith(_tenantContext.TenantId + ":"))
                throw new ArgumentException("WorkflowId must start with tenantId");
            
            return request.WorkflowId;
        }

        if (!string.IsNullOrEmpty(request.WorkflowType))
        {
            return $"{_tenantContext.TenantId}:{request.WorkflowType}";
        }

        throw new ArgumentException("Either WorkflowId or WorkflowType must be provided");
    }

    private async Task<BotResponse> ProcessSynchronousBotAsync(BotRequest request, ConversationThreadInfo threadInfo)
    {
        var workflowIdentifier = new WorkflowIdentifier(EnsureWorkflowId(request), _tenantContext);
        var updateRequest = new 
        {
            SourceAgent = workflowIdentifier.AgentName,
            SourceWorkflowId = workflowIdentifier.WorkflowId,
            SourceWorkflowType = workflowIdentifier.WorkflowType,

            Payload = new {
                 Agent = workflowIdentifier.AgentName,
                 request.ThreadId,
                 request.ParticipantId,
                 Type = MessageType.Chat.ToString(),
                 request.Text, 
                 request.RequestId,
                 request.Scope,
                 request.Hint,
                 request.Data,
                 request.Authorization
            }
        };

        return await HandleTemporalUpdate(EnsureWorkflowId(request), updateRequest);

    }

    public async Task<BotResponse> HandleTemporalUpdate(string workflow, object args)
    {
        var procedureName = Constants.UPDATE_INBOUND_CHAT_OR_DATA;
        try
        {
            _logger.LogInformation($"Temporal update for workflow {workflow} with procedure {procedureName} and args {args}");

            var workflowIdentifier = new WorkflowIdentifier(workflow, _tenantContext);

            var client = _temporalClientService.GetClient();

            var workflowOptions = new NewWorkflowOptions(
                    workflowIdentifier.AgentName,
                    workflowIdentifier.WorkflowType,
                    workflowIdentifier.WorkflowId,
                    _tenantContext);

            var withStartWorkflowOperation = WithStartWorkflowOperation.Create(
                workflowIdentifier.WorkflowType,
                [],
                workflowOptions
            );

            var workflowUpdateWithStartOptions = new WorkflowUpdateWithStartOptions(
                withStartWorkflowOperation
            );

            var response = await client.ExecuteUpdateWithStartWorkflowAsync<BotResponse>(
                procedureName,
                [args],
                workflowUpdateWithStartOptions
                );

            // Ensure required properties are set
            response.IsComplete = true;
            response.Timestamp = DateTime.UtcNow;

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling RPC request");
            throw;
        }
    }

    private async Task SaveRequestMessageAsync(
        BotRequest request)
    {
        var optimizedRequest = new MessageRequest
        {
            TenantId = _tenantContext.TenantId,
            ThreadId = request.ThreadId!,
            ParticipantId = request.ParticipantId,
            WorkflowId = request.WorkflowId ?? throw new Exception("WorkflowId is required"),
            WorkflowType = request.WorkflowType ?? throw new Exception("WorkflowType is required"),
            CreatedBy = _tenantContext.LoggedInUser,
            Direction = MessageDirection.Incoming,
            MessageType = MessageType.Chat,
            RequestId = request.RequestId,
            Text = request.Text,
            Data = request.Data,
            Hint = request.Hint,
            Scope = request.Scope
        };

        await _conversationRepository.SaveMessageAsync(optimizedRequest);
    }

    private async Task SaveResponseMessageAsync(
        BotResponse response)
    {
        var optimizedRequest = new MessageRequest
        {
            TenantId = _tenantContext.TenantId,
            ThreadId = response.ThreadId ?? throw new Exception("ThreadId is required"),
            ParticipantId = response.ParticipantId ?? throw new Exception("ParticipantId is required"),
            WorkflowId = response.WorkflowId ?? throw new Exception("WorkflowId is required"),
            WorkflowType = response.WorkflowType ?? throw new Exception("WorkflowType is required"),
            CreatedBy = _tenantContext.LoggedInUser,
            Direction = MessageDirection.Outgoing,
            MessageType = MessageType.Chat,
            RequestId = response.RequestId,
            Text = response.Text,
            Data = response.Data,
            Scope = response.Scope
        };

        await _conversationRepository.SaveMessageAsync(optimizedRequest);
    }

    private static string ExtractWorkflowTypeFromId(string workflowId)
    {
        var parts = workflowId.Split(':');
        if (parts.Length > 2)
        {
            return $"{parts[1]}:{parts[2]}";
        }
        throw new ArgumentException("Invalid WorkflowId format", nameof(workflowId));
    }

    #endregion
} 