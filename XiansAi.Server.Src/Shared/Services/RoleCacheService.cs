using Microsoft.Extensions.Caching.Memory;
using Shared.Repositories;

namespace Shared.Services
{
    public interface IRoleCacheService
    {
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
                roles = await _userRepository.GetUserRolesAsync(userId, tenantId);
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(_cacheDuration)
                    .SetSize(1);
                _cache.Set(cacheKey, roles, cacheOptions);
            }
            
            // Filter out TenantParticipant role - participants cannot authenticate/login
            // They exist in the system for Admin API queries only
            var filteredRoles = (roles ?? new List<string>())
                .Where(r => r != SystemRoles.TenantParticipant)
                .ToList();
            
            return filteredRoles;
        }

        public void InvalidateUserRoles(string userId, string tenantId)
        {
            var cacheKey = $"{tenantId}:{userId}:roles";
            _cache.Remove(cacheKey);
        }
    }
}
