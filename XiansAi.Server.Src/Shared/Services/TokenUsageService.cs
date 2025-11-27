using Microsoft.Extensions.Logging;
using Shared.Configuration;
using Shared.Data.Models.Usage;
using Shared.Repositories;

namespace Shared.Services;

public record TokenUsageStatus(
    bool Enabled,
    long MaxTokens,
    int WindowSeconds,
    long TokensUsed,
    long TokensRemaining,
    DateTime WindowStart,
    DateTime WindowEndsAt,
    bool IsExceeded);

public record TokenUsageRecord(
    string TenantId,
    string UserId,
    string? Model,
    long PromptTokens,
    long CompletionTokens,
    string? WorkflowId,
    string? RequestId,
    string? Source,
    Dictionary<string, string>? Metadata);

public interface ITokenUsageService
{
    Task<TokenUsageStatus> CheckAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
    Task RecordAsync(TokenUsageRecord record, CancellationToken cancellationToken = default);
}

public class TokenUsageService : ITokenUsageService
{
    private readonly ITokenUsageLimitRepository _limitRepository;
    private readonly ITokenUsageWindowRepository _windowRepository;
    private readonly ITokenUsageEventRepository _eventRepository;
    private readonly TokenUsageOptions _options;
    private readonly ILogger<TokenUsageService> _logger;

    public TokenUsageService(
        ITokenUsageLimitRepository limitRepository,
        ITokenUsageWindowRepository windowRepository,
        ITokenUsageEventRepository eventRepository,
        TokenUsageOptions options,
        ILogger<TokenUsageService> logger)
    {
        _limitRepository = limitRepository;
        _windowRepository = windowRepository;
        _eventRepository = eventRepository;
        _options = options;
        _logger = logger;
    }

    public async Task<TokenUsageStatus> CheckAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return DisabledStatus();
        }

        var (effectiveLimit, windowStart) = await ResolveLimitAsync(tenantId, userId, cancellationToken);
        if (effectiveLimit == null)
        {
            return DisabledStatus();
        }

        var usageWindow = await _windowRepository.GetWindowAsync(
            tenantId,
            userId,
            windowStart,
            effectiveLimit.WindowSeconds,
            cancellationToken);

        var tokensUsed = usageWindow?.TokensUsed ?? 0;
        var maxTokens = effectiveLimit.MaxTokens;
        var remaining = Math.Max(0, maxTokens - tokensUsed);
        var endsAt = windowStart.AddSeconds(effectiveLimit.WindowSeconds);
        var isExceeded = remaining <= 0;

        if (isExceeded)
        {
            _logger.LogWarning("Token usage exceeded for tenant {TenantId}, user {UserId}. Limit={Limit}, Used={Used}", tenantId, userId, maxTokens, tokensUsed);
        }
        else
        {
            var warningThreshold = (long)(maxTokens * _options.WarningPercentage);
            if (tokensUsed >= warningThreshold)
            {
                _logger.LogInformation("Token usage warning for tenant {TenantId}, user {UserId}. Used={Used}/{Limit}", tenantId, userId, tokensUsed, maxTokens);
            }
        }

        return new TokenUsageStatus(
            Enabled: true,
            MaxTokens: maxTokens,
            WindowSeconds: effectiveLimit.WindowSeconds,
            TokensUsed: tokensUsed,
            TokensRemaining: remaining,
            WindowStart: windowStart,
            WindowEndsAt: endsAt,
            IsExceeded: isExceeded);
    }

    public async Task RecordAsync(TokenUsageRecord record, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var totalTokens = Math.Max(0, record.PromptTokens) + Math.Max(0, record.CompletionTokens);
        if (totalTokens == 0)
        {
            return;
        }

        var (effectiveLimit, windowStart) = await ResolveLimitAsync(record.TenantId, record.UserId, cancellationToken);
        if (effectiveLimit == null)
        {
            return;
        }

        _logger.LogInformation(
            "Recording token usage: tenant={TenantId}, user={UserId}, totalTokens={TotalTokens}, prompt={PromptTokens}, completion={CompletionTokens}, windowStart={WindowStart:o}, windowSeconds={WindowSeconds}",
            record.TenantId,
            record.UserId,
            totalTokens,
            record.PromptTokens,
            record.CompletionTokens,
            windowStart,
            effectiveLimit.WindowSeconds);

        await _windowRepository.IncrementWindowAsync(
            record.TenantId,
            record.UserId,
            windowStart,
            effectiveLimit.WindowSeconds,
            totalTokens,
            cancellationToken);

        if (_options.RecordUsageEvents)
        {
            var usageEvent = new TokenUsageEvent
            {
                TenantId = record.TenantId,
                UserId = record.UserId,
                Model = record.Model,
                PromptTokens = record.PromptTokens,
                CompletionTokens = record.CompletionTokens,
                TotalTokens = totalTokens,
                WorkflowId = record.WorkflowId,
                RequestId = record.RequestId,
                Source = record.Source,
                Metadata = record.Metadata,
                CreatedAt = DateTime.UtcNow
            };

            await _eventRepository.InsertAsync(usageEvent, cancellationToken);
        }
    }

    private async Task<(TokenUsageLimit? limit, DateTime windowStart)> ResolveLimitAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        var limit = await _limitRepository.GetEffectiveLimitAsync(tenantId, userId, cancellationToken);

        if (limit == null)
        {
            if (_options.DefaultTenantLimit <= 0)
            {
                return (null, default);
            }

            limit = new TokenUsageLimit
            {
                TenantId = tenantId,
                UserId = null,
                MaxTokens = _options.DefaultTenantLimit,
                WindowSeconds = _options.WindowSeconds,
                Enabled = true,
                EffectiveFrom = DateTime.UnixEpoch
            };
        }

        var now = DateTime.UtcNow;
        var referencePoint = limit.EffectiveFrom;

        if (referencePoint == default)
        {
            referencePoint = DateTime.UnixEpoch;
        }
        else if (referencePoint > now)
        {
            referencePoint = now;
        }

        var elapsedSeconds = Math.Max(0, (now - referencePoint).TotalSeconds);
        var completedWindows = (long)Math.Floor(elapsedSeconds / limit.WindowSeconds);
        var windowStart = referencePoint.AddSeconds(completedWindows * (long)limit.WindowSeconds);

        return (limit, windowStart);
    }

    private static TokenUsageStatus DisabledStatus() =>
        new(
            Enabled: false,
            MaxTokens: long.MaxValue,
            WindowSeconds: 0,
            TokensUsed: 0,
            TokensRemaining: long.MaxValue,
            WindowStart: DateTime.MinValue,
            WindowEndsAt: DateTime.MaxValue,
            IsExceeded: false);
}

