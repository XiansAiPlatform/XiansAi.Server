using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services;

/// <summary>
/// Drops every cached authorization decision that depends on an account, for use when that
/// account changes in a way that should take effect now rather than when the caches expire.
///
/// Disabling an account is checked when a decision is made, not when it is used, and each API
/// caches the decision: the User API keeps the approved tenant list, the Agent API keeps the
/// certificate's user and roles, the Admin API keeps the resolved roles, and validated tokens are
/// kept for all of them. Without this, a disabled account keeps working for as long as the longest
/// of those lifetimes.
/// </summary>
public interface IUserAuthorizationInvalidator
{
    Task InvalidateAsync(User user);
    Task InvalidateAsync(string userId);
}

public class UserAuthorizationInvalidator : IUserAuthorizationInvalidator
{
    private readonly IUserCacheIndex _userCacheIndex;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserAuthorizationInvalidator> _logger;

    public UserAuthorizationInvalidator(
        IUserCacheIndex userCacheIndex,
        IUserRepository userRepository,
        ILogger<UserAuthorizationInvalidator> logger)
    {
        _userCacheIndex = userCacheIndex;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task InvalidateAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var user = await _userRepository.GetByUserIdAsync(userId);
        if (user == null)
        {
            _userCacheIndex.Invalidate(userId);
            return;
        }

        await InvalidateAsync(user);
    }

    public async Task InvalidateAsync(User user)
    {
        _userCacheIndex.Invalidate(user.UserId);

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        // Credentials that carry only an address resolve through every account holding it, and the
        // result changes when one of them does: a disabled account drops out of the combined roles
        // and withdraws the administrator role from the rest. Those decisions are cached against a
        // sibling's id, so they survive unless the siblings are invalidated too.
        try
        {
            var siblings = await _userRepository.GetAllByUserEmailAsync(user.Email);
            foreach (var sibling in siblings)
            {
                if (!string.Equals(sibling.UserId, user.UserId, StringComparison.Ordinal))
                {
                    _userCacheIndex.Invalidate(sibling.UserId);
                }
            }
        }
        catch (Exception ex)
        {
            // The account's own entries are already gone, which is the part that matters most.
            _logger.LogWarning(ex,
                "Could not invalidate cached authorization for the accounts sharing {Email}",
                LogSanitizer.RedactEmail(user.Email));
        }
    }
}
