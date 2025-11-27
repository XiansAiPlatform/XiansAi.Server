using Shared.Auth;
using Shared.Configuration;
using Shared.Data.Models.Usage;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Shared.Services;

public interface ITokenUsageAdminService
{
    Task<ServiceResult<TokenUsageStatus>> GetStatusAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<TokenUsageLimit>>> GetLimitsAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<ServiceResult<TokenUsageLimit>> UpsertLimitAsync(UpsertTokenUsageLimitRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult> DeleteLimitAsync(string limitId, CancellationToken cancellationToken = default);
}

public record UpsertTokenUsageLimitRequest(
    string TenantId,
    string? UserId,
    long MaxTokens,
    int WindowSeconds,
    bool Enabled,
    string UpdatedBy);

public class TokenUsageAdminService : ITokenUsageAdminService
{
    private readonly ITokenUsageLimitRepository _limitRepository;
    private readonly ITokenUsageService _usageService;
    private readonly TokenUsageOptions _options;
    private readonly ILogger<TokenUsageAdminService> _logger;

    public TokenUsageAdminService(
        ITokenUsageLimitRepository limitRepository,
        ITokenUsageService usageService,
        TokenUsageOptions options,
        ILogger<TokenUsageAdminService> logger)
    {
        _limitRepository = limitRepository;
        _usageService = usageService;
        _options = options;
        _logger = logger;
    }

    public async Task<ServiceResult<TokenUsageStatus>> GetStatusAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var status = await _usageService.CheckAsync(tenantId, userId, cancellationToken);
        return ServiceResult<TokenUsageStatus>.Success(status);
    }

    public async Task<ServiceResult<List<TokenUsageLimit>>> GetLimitsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var limits = await _limitRepository.GetLimitsForTenantAsync(tenantId, cancellationToken);
        return ServiceResult<List<TokenUsageLimit>>.Success(limits);
    }

    public async Task<ServiceResult<TokenUsageLimit>> UpsertLimitAsync(UpsertTokenUsageLimitRequest request, CancellationToken cancellationToken = default)
    {
        if (request.MaxTokens <= 0)
        {
            return ServiceResult<TokenUsageLimit>.BadRequest("MaxTokens must be greater than zero.");
        }

        if (request.WindowSeconds < 60)
        {
            return ServiceResult<TokenUsageLimit>.BadRequest("WindowSeconds must be at least 60.");
        }

        // Check if an existing limit exists to preserve EffectiveFrom when only toggling Enabled
        TokenUsageLimit? existingLimit = null;
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            existingLimit = await _limitRepository.GetTenantLimitAsync(request.TenantId, cancellationToken);
        }
        else
        {
            existingLimit = await _limitRepository.GetUserLimitAsync(request.TenantId, request.UserId, cancellationToken);
        }

        // Determine if we need a new window (only when maxTokens or windowSeconds change)
        bool needsNewWindow = existingLimit == null ||
            existingLimit.MaxTokens != request.MaxTokens ||
            existingLimit.WindowSeconds != request.WindowSeconds;

        // Preserve existing EffectiveFrom when only toggling Enabled or other non-window fields
        // Only reset to now when creating new limit or window-affecting fields changed
        DateTime effectiveFrom;
        if (needsNewWindow)
        {
            effectiveFrom = DateTime.UtcNow;
        }
        else if (existingLimit != null && existingLimit.EffectiveFrom != default)
        {
            effectiveFrom = existingLimit.EffectiveFrom;
        }
        else
        {
            effectiveFrom = DateTime.UtcNow;
        }

        var limit = new TokenUsageLimit
        {
            TenantId = request.TenantId,
            UserId = string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId,
            MaxTokens = request.MaxTokens,
            WindowSeconds = request.WindowSeconds,
            Enabled = request.Enabled,
            EffectiveFrom = effectiveFrom,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = request.UpdatedBy
        };

        var saved = await _limitRepository.UpsertAsync(limit, cancellationToken);
        return ServiceResult<TokenUsageLimit>.Success(saved);
    }

    public async Task<ServiceResult> DeleteLimitAsync(string limitId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(limitId))
        {
            return ServiceResult.Failure("LimitId is required.");
        }

        var deleted = await _limitRepository.DeleteAsync(limitId, cancellationToken);
        return deleted
            ? ServiceResult.Success()
            : ServiceResult.Failure("Limit not found.", StatusCode.NotFound);
    }
}

