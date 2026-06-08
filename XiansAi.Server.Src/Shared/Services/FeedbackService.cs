using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

public class SubmitMessageFeedbackRequest
{
    [JsonPropertyName("messageId")]
    public required string MessageId { get; set; }

    [JsonPropertyName("threadId")]
    public required string ThreadId { get; set; }

    [JsonPropertyName("agentName")]
    public required string AgentName { get; set; }

    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; set; }

    [JsonPropertyName("workflowType")]
    public required string WorkflowType { get; set; }

    private string _participantId = string.Empty;

    [JsonPropertyName("participantId")]
    public required string ParticipantId
    {
        get => _participantId;
        set => _participantId = value?.ToLowerInvariant() ?? string.Empty;
    }

    [JsonPropertyName("starRating")]
    public int StarRating { get; set; }

    [JsonPropertyName("reasonCategory")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FeedbackReasonCategory? ReasonCategory { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public interface IFeedbackService
{
    Task<ServiceResult<string>> SubmitFeedbackAsync(SubmitMessageFeedbackRequest request);
    Task<List<ConversationMessageDto>> BuildMessagesWithFeedbackAsync(IReadOnlyList<ConversationMessage> messages, string tenantId);
}

public class FeedbackService : IFeedbackService
{
    private readonly IFeedbackRepository _feedbackRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(
        IFeedbackRepository feedbackRepository,
        IConversationRepository conversationRepository,
        ITenantContext tenantContext,
        ILogger<FeedbackService> logger)
    {
        _feedbackRepository = feedbackRepository;
        _conversationRepository = conversationRepository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<ServiceResult<string>> SubmitFeedbackAsync(SubmitMessageFeedbackRequest request)
    {
        if (request.StarRating is < 1 or > 5)
        {
            return ServiceResult<string>.BadRequest("Star rating must be between 1 and 5.");
        }

        if (request.StarRating < 4 && request.ReasonCategory == null)
        {
            return ServiceResult<string>.BadRequest("reasonCategory is required when star rating is below 4.");
        }

        if (request.StarRating < 4
            && request.ReasonCategory == FeedbackReasonCategory.Other
            && string.IsNullOrWhiteSpace(request.Comment))
        {
            return ServiceResult<string>.BadRequest("comment is required when reason category is Other.");
        }

        var tenantId = _tenantContext.TenantId;
        var existing = await _feedbackRepository.GetFeedbackByMessageIdAsync(request.MessageId, tenantId);
        if (existing != null)
        {
            return ServiceResult<string>.Conflict("Feedback has already been submitted for this message.");
        }

        var message = await _conversationRepository.GetMessageByIdAsync(request.MessageId, tenantId);
        if (message == null)
        {
            return ServiceResult<string>.NotFound("Message not found.");
        }

        if (!string.Equals(message.ThreadId, request.ThreadId, StringComparison.Ordinal))
        {
            return ServiceResult<string>.BadRequest("threadId does not match the message.");
        }

        if (!string.Equals(message.WorkflowId, request.WorkflowId, StringComparison.Ordinal))
        {
            return ServiceResult<string>.BadRequest("workflowId does not match the message.");
        }

        if (!string.Equals(message.WorkflowType, request.WorkflowType, StringComparison.Ordinal))
        {
            return ServiceResult<string>.BadRequest("workflowType does not match the message.");
        }

        if (!string.Equals(message.ParticipantId, request.ParticipantId, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<string>.BadRequest("participantId does not match the message.");
        }

        if (message.Direction != MessageDirection.Outgoing)
        {
            return ServiceResult<string>.BadRequest("Feedback can only be submitted for agent (outgoing) messages.");
        }

        var expectedAgent = new WorkflowIdentifier(message.WorkflowId, _tenantContext).AgentName;
        string validatedAgentName;
        try
        {
            validatedAgentName = Agent.SanitizeAndValidateName(request.AgentName);
        }
        catch (ValidationException ex)
        {
            return ServiceResult<string>.BadRequest(ex.Message);
        }

        if (!string.Equals(validatedAgentName, expectedAgent, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<string>.BadRequest("agentName does not match the message workflow.");
        }

        var now = DateTime.UtcNow;
        var doc = new MessageFeedbackDocument
        {
            MessageId = request.MessageId,
            ThreadId = request.ThreadId,
            TenantId = tenantId,
            AgentName = validatedAgentName,
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType,
            ParticipantId = message.ParticipantId,
            StarRating = request.StarRating,
            ReasonCategory = request.StarRating < 4 ? request.ReasonCategory : null,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            SubmittedBy = _tenantContext.LoggedInUser,
            SubmittedAt = now,
            CreatedAt = now
        };

        try
        {
            var id = await _feedbackRepository.SaveFeedbackAsync(doc);
            _logger.LogInformation("Saved message feedback {FeedbackId} for message {MessageId}", LogSanitizer.Sanitize(id), LogSanitizer.Sanitize(request.MessageId));
            return ServiceResult<string>.Success(id, StatusCode.Created);
        }
        catch (MongoWriteException ex) when (
            ex.WriteError?.Code == 11000
            || ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogDebug(ex, "Duplicate feedback insert for message {MessageId} (tenant unique index)", LogSanitizer.Sanitize(request.MessageId));
            return ServiceResult<string>.Conflict("Feedback has already been submitted for this message.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save feedback for message {MessageId}", LogSanitizer.Sanitize(request.MessageId));
            return ServiceResult<string>.InternalServerError("Failed to save feedback.");
        }
    }

    public async Task<List<ConversationMessageDto>> BuildMessagesWithFeedbackAsync(
        IReadOnlyList<ConversationMessage> messages,
        string tenantId)
    {
        if (messages.Count == 0)
        {
            return new List<ConversationMessageDto>();
        }

        var ids = messages.Select(m => m.Id).ToList();
        var feedbackMap = await _feedbackRepository.GetFeedbackByMessageIdsAsync(ids, tenantId);

        var result = new List<ConversationMessageDto>(messages.Count);
        foreach (var m in messages)
        {
            var dto = CopyToDto(m);
            if (feedbackMap.TryGetValue(m.Id, out var fb))
            {
                dto.Feedback = new FeedbackDto
                {
                    StarRating = fb.StarRating,
                    ReasonCategory = fb.ReasonCategory,
                    Comment = fb.Comment,
                    SubmittedBy = fb.SubmittedBy,
                    SubmittedAt = fb.SubmittedAt
                };
            }

            result.Add(dto);
        }

        return result;
    }

    private static ConversationMessageDto CopyToDto(ConversationMessage source)
    {
        return new ConversationMessageDto
        {
            Id = source.Id,
            ThreadId = source.ThreadId,
            RequestId = source.RequestId,
            TenantId = source.TenantId,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            CreatedBy = source.CreatedBy,
            Direction = source.Direction,
            Text = source.Text,
            Status = source.Status,
            Data = source.Data,
            ParticipantId = source.ParticipantId,
            Scope = source.Scope,
            Hint = source.Hint,
            TaskId = source.TaskId,
            WorkflowId = source.WorkflowId,
            WorkflowType = source.WorkflowType,
            MessageType = source.MessageType,
            Origin = source.Origin,
            ExpiresAt = source.ExpiresAt
        };
    }
}
