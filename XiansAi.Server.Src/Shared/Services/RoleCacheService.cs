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
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        public RoleCacheService(
            IMemoryCache cache,
            IUserRepository userRepository)
        {
            _cache = cache;
            _userRepository = userRepository;
        }

        public async Task<List<string>> GetUserRolesAsync(string userId, string tenantId)
        {
            var cacheKey = $"{tenantId}:{userId}:roles";
            if (!_cache.TryGetValue(cacheKey, out List<string>? roles))
            {
                var rawRoles = await _userRepository.GetUserRolesAsync(userId, tenantId);
                // Filter before caching - participants cannot authenticate/login (exist for Admin API queries only)
                roles = (rawRoles ?? new List<string>())
                    .Where(r => r != SystemRoles.TenantParticipant && r != SystemRoles.TenantParticipantAdmin)
                    .ToList();
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(_cacheDuration)
                    .SetSize(1);
                _cache.Set(cacheKey, roles, cacheOptions);
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
