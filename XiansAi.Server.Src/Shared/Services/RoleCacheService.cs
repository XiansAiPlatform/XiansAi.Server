using Microsoft.Extensions.Caching.Memory;
using Shared.Repositories;

namespace Shared.Services
{
    public interface IRoleCacheService
    {
        /// <summary>
        /// Returns roles for the user in the given tenant, excluding TenantParticipant and TenantParticipantAdmin.
        /// Participants are filtered out because they cannot authenticate/login via OIDC; they exist for Admin API queries only.
        /// For unfiltered roles including participants, use IUserRepository.GetUserRolesAsync directly.
        /// </summary>
        Task<List<string>> GetUserRolesAsync(string userId, string tenantId);
        void InvalidateUserRoles(string userId, string tenantId);
    }

    public class RoleCacheService : IRoleCacheService
    {
        private readonly IMemoryCache _cache;
        private readonly IUserRepository _userRepository;
        private readonly IUserCacheIndex _userCacheIndex;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        public RoleCacheService(
            IMemoryCache cache,
            IUserRepository userRepository,
            IUserCacheIndex userCacheIndex)
        {
            _cache = cache;
            _userRepository = userRepository;
            _userCacheIndex = userCacheIndex;
        }

        public async Task<List<string>> GetUserRolesAsync(string userId, string tenantId)
        {
            // Entries are keyed on the value passed in, but every InvalidateUserRoles caller passes a
            // canonical user id, so an entry keyed on an email would never be invalidated and would
            // serve stale roles for the full cache duration. Callers resolve an email to an account
            // before reaching here; this keeps a caller that forgets from creating such an entry.
            if (userId.Contains('@'))
            {
                return SystemRoles.ExcludingParticipantRoles(
                    await _userRepository.GetUserRolesAsync(userId, tenantId) ?? new List<string>());
            }

            var cacheKey = $"{tenantId}:{userId}:roles";
            if (!_cache.TryGetValue(cacheKey, out List<string>? roles))
            {
                var rawRoles = await _userRepository.GetUserRolesAsync(userId, tenantId);
                // Filter before caching - participants cannot authenticate/login (exist for Admin API queries only)
                roles = SystemRoles.ExcludingParticipantRoles(rawRoles ?? new List<string>());
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(_cacheDuration)
                    .SetSize(1)
                    .RegisterPostEvictionCallback(
                        (key, _, _, _) => _userCacheIndex.Forget(userId, key.ToString() ?? string.Empty));
                _cache.Set(cacheKey, roles, cacheOptions);

                // Tracked so that disabling the account drops its roles in every tenant, including
                // any this user is no longer a member of — those are absent from the record the
                // caller would otherwise iterate to work out which keys to remove.
                _userCacheIndex.Track(userId, cacheKey);
            }

            return roles ?? new List<string>();
        }

        public void InvalidateUserRoles(string userId, string tenantId)
        {
            var cacheKey = $"{tenantId}:{userId}:roles";
            _cache.Remove(cacheKey);
        }
    }
}
