using System.Text.Json.Serialization;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Filter for admin feedback queries. All properties are optional; date filters apply to the
/// time the feedback was submitted.
/// </summary>
public sealed class FeedbackFilter
{
    public int? Rating { get; set; }
    public string? AgentName { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>Number of feedback entries for a single star rating (1-5).</summary>
public sealed class RatingCount
{
    [JsonPropertyName("rating")]
    public int Rating { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>Number of feedback entries for a single reason category.</summary>
public sealed class ReasonCategoryCount
{
    [JsonPropertyName("category")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FeedbackReasonCategory Category { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>Aggregated feedback statistics used for dashboards.</summary>
public sealed class FeedbackStatsResponse
{
    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("averageRating")]
    public double AverageRating { get; set; }

    /// <summary>Always contains an entry for every rating 1-5 (count 0 when none), ascending by rating.</summary>
    [JsonPropertyName("ratingCounts")]
    public List<RatingCount> RatingCounts { get; set; } = new();

    /// <summary>Reason categories that have at least one entry, descending by count.</summary>
    [JsonPropertyName("reasonCategoryCounts")]
    public List<ReasonCategoryCount> ReasonCategoryCounts { get; set; } = new();
}

/// <summary>A page of feedback entries.</summary>
public sealed class FeedbackListResponse
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<MessageFeedbackDocument> Items { get; set; }

    [JsonPropertyName("totalCount")]
    public required long TotalCount { get; set; }

    [JsonPropertyName("page")]
    public required int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public required int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public required int TotalPages { get; set; }
}

/// <summary>
/// A single feedback entry together with the surrounding thread messages so an admin can understand
/// the context in which the rating was given.
/// </summary>
public sealed class FeedbackDetailResponse
{
    [JsonPropertyName("feedback")]
    public required MessageFeedbackDocument Feedback { get; set; }

    /// <summary>Id of the rated message within <see cref="Messages"/>.</summary>
    [JsonPropertyName("ratedMessageId")]
    public required string RatedMessageId { get; set; }

    /// <summary>Thread messages ordered chronologically, including the rated message and its neighbours.</summary>
    [JsonPropertyName("messages")]
    public required IReadOnlyList<ConversationMessageDto> Messages { get; set; }
}

public interface IFeedbackQueryService
{
    Task<ServiceResult<FeedbackStatsResponse>> GetStatsAsync(string tenantId, FeedbackFilter filter);
    Task<ServiceResult<FeedbackListResponse>> ListAsync(string tenantId, FeedbackFilter filter, int page, int pageSize);
    Task<ServiceResult<FeedbackDetailResponse>> GetDetailAsync(
        string tenantId, string feedbackId, int contextBefore, int contextAfter, bool chatOnly);
}

/// <summary>
/// Read-only service that powers the admin feedback visualization endpoints: aggregate statistics,
/// a filterable/paginated list, and a per-feedback detail view with surrounding thread context.
/// All operations are tenant-scoped via the supplied tenantId (resolved from the admin route).
/// </summary>
public sealed class FeedbackQueryService : IFeedbackQueryService
{
    private const int MaxPageSize = 100;
    private const int MaxContextMessages = 50;

    private readonly IFeedbackRepository _feedbackRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly IFeedbackService _feedbackService;
    private readonly ILogger<FeedbackQueryService> _logger;

    public FeedbackQueryService(
        IFeedbackRepository feedbackRepository,
        IConversationRepository conversationRepository,
        IFeedbackService feedbackService,
        ILogger<FeedbackQueryService> logger)
    {
        _feedbackRepository = feedbackRepository ?? throw new ArgumentNullException(nameof(feedbackRepository));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
        _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<FeedbackStatsResponse>> GetStatsAsync(string tenantId, FeedbackFilter filter)
    {
        var validation = ValidateFilter(tenantId, filter);
        if (validation != null)
        {
            return ServiceResult<FeedbackStatsResponse>.BadRequest(validation);
        }

        try
        {
            var stats = await _feedbackRepository.GetFeedbackStatsAsync(ToQuery(tenantId, filter));

            var response = new FeedbackStatsResponse
            {
                Total = stats.Total,
                AverageRating = stats.AverageRating,
                RatingCounts = BuildRatingCounts(stats.RatingCounts),
                ReasonCategoryCounts = stats.ReasonCategoryCounts
                    .Select(r => new ReasonCategoryCount { Category = r.Category, Count = r.Count })
                    .OrderByDescending(r => r.Count)
                    .ToList()
            };

            return ServiceResult<FeedbackStatsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve feedback stats for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<FeedbackStatsResponse>.InternalServerError("Failed to retrieve feedback statistics.");
        }
    }

    public async Task<ServiceResult<FeedbackListResponse>> ListAsync(
        string tenantId, FeedbackFilter filter, int page, int pageSize)
    {
        var validation = ValidateFilter(tenantId, filter);
        if (validation != null)
        {
            return ServiceResult<FeedbackListResponse>.BadRequest(validation);
        }

        if (page < 1)
        {
            return ServiceResult<FeedbackListResponse>.BadRequest("page must be 1 or greater.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            return ServiceResult<FeedbackListResponse>.BadRequest($"pageSize must be between 1 and {MaxPageSize}.");
        }

        try
        {
            var (items, totalCount) = await _feedbackRepository.QueryFeedbackAsync(
                ToQuery(tenantId, filter), page, pageSize);

            var totalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0;

            var response = new FeedbackListResponse
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages
            };

            return ServiceResult<FeedbackListResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list feedback for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<FeedbackListResponse>.InternalServerError("Failed to retrieve feedback.");
        }
    }

    public async Task<ServiceResult<FeedbackDetailResponse>> GetDetailAsync(
        string tenantId, string feedbackId, int contextBefore, int contextAfter, bool chatOnly)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResult<FeedbackDetailResponse>.BadRequest("tenantId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(feedbackId))
        {
            return ServiceResult<FeedbackDetailResponse>.BadRequest("feedbackId cannot be empty.");
        }

        if (contextBefore < 0 || contextBefore > MaxContextMessages
            || contextAfter < 0 || contextAfter > MaxContextMessages)
        {
            return ServiceResult<FeedbackDetailResponse>.BadRequest(
                $"contextBefore and contextAfter must be between 0 and {MaxContextMessages}.");
        }

        try
        {
            var feedback = await _feedbackRepository.GetFeedbackByIdAsync(feedbackId, tenantId);
            if (feedback == null)
            {
                return ServiceResult<FeedbackDetailResponse>.NotFound("Feedback not found.");
            }

            var contextMessages = await _conversationRepository.GetThreadContextAroundMessageAsync(
                tenantId, feedback.ThreadId, feedback.MessageId, contextBefore, contextAfter, chatOnly);

            // Attach any existing feedback to the context messages so the admin sees ratings inline.
            var messages = await _feedbackService.BuildMessagesWithFeedbackAsync(contextMessages, tenantId);

            var response = new FeedbackDetailResponse
            {
                Feedback = feedback,
                RatedMessageId = feedback.MessageId,
                Messages = messages
            };

            return ServiceResult<FeedbackDetailResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve feedback detail {FeedbackId} for tenant {TenantId}",
                LogSanitizer.Sanitize(feedbackId), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<FeedbackDetailResponse>.InternalServerError("Failed to retrieve feedback detail.");
        }
    }

    /// <summary>Validates the shared filter. Returns an error message, or null when valid.</summary>
    private static string? ValidateFilter(string tenantId, FeedbackFilter filter)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return "tenantId cannot be empty.";
        }

        if (filter.Rating is < 1 or > 5)
        {
            return "rating must be between 1 and 5.";
        }

        if (filter.StartDate.HasValue && filter.EndDate.HasValue && filter.StartDate.Value > filter.EndDate.Value)
        {
            return "startDate cannot be after endDate.";
        }

        return null;
    }

    private static FeedbackQuery ToQuery(string tenantId, FeedbackFilter filter) => new()
    {
        TenantId = tenantId,
        StarRating = filter.Rating,
        AgentName = string.IsNullOrWhiteSpace(filter.AgentName) ? null : filter.AgentName.Trim(),
        StartDate = filter.StartDate,
        EndDate = filter.EndDate
    };

    /// <summary>Expands raw rating buckets to always include all ratings 1-5 (zero-filled), ascending.</summary>
    private static List<RatingCount> BuildRatingCounts(IEnumerable<RatingCountBucket> buckets)
    {
        var byRating = buckets.ToDictionary(b => b.Rating, b => b.Count);
        var result = new List<RatingCount>(5);
        for (var rating = 1; rating <= 5; rating++)
        {
            result.Add(new RatingCount
            {
                Rating = rating,
                Count = byRating.TryGetValue(rating, out var count) ? count : 0
            });
        }

        return result;
    }
}
