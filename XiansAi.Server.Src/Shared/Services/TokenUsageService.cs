using Microsoft.Extensions.Logging;
using Shared.Configuration;
using Shared.Data.Models.Usage;
using Shared.Repositories;

namespace Shared.Services;

public record TokenUsageRecord(
    string TenantId,
    string UserId,
    string? Model,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    long MessageCount,
    string? WorkflowId,
    string? RequestId,
    string? Source,
    Dictionary<string, string>? Metadata,
    long? ResponseTimeMs = null);

public interface ITokenUsageService
{
    Task RecordAsync(TokenUsageRecord record, CancellationToken cancellationToken = default);
}

public class TokenUsageService : ITokenUsageService
{
    private readonly ITokenUsageEventRepository _eventRepository;
    private readonly TokenUsageOptions _options;
    private readonly ILogger<TokenUsageService> _logger;

    public TokenUsageService(
        ITokenUsageEventRepository eventRepository,
        TokenUsageOptions options,
        ILogger<TokenUsageService> logger)
    {
        _eventRepository = eventRepository;
        _options = options;
        _logger = logger;
    }

    public async Task RecordAsync(TokenUsageRecord record, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_options.RecordUsageEvents)
        {
            return;
        }

        if (record.TotalTokens == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Recording token usage: tenant={TenantId}, user={UserId}, totalTokens={TotalTokens}, prompt={PromptTokens}, completion={CompletionTokens}, responseTimeMs={ResponseTimeMs}",
            record.TenantId,
            record.UserId,
            record.TotalTokens,
            record.PromptTokens,
            record.CompletionTokens,
            record.ResponseTimeMs);

        var usageEvent = new TokenUsageEvent
        {
            TenantId = record.TenantId,
            UserId = record.UserId,
            Model = record.Model,
            PromptTokens = record.PromptTokens,
            CompletionTokens = record.CompletionTokens,
            TotalTokens = record.TotalTokens,
            MessageCount = record.MessageCount,
            WorkflowId = record.WorkflowId,
            RequestId = record.RequestId,
            Source = record.Source,
            Metadata = record.Metadata,
            ResponseTimeMs = record.ResponseTimeMs,
            CreatedAt = DateTime.UtcNow
        };

        await _eventRepository.InsertAsync(usageEvent, cancellationToken);
    }
}

