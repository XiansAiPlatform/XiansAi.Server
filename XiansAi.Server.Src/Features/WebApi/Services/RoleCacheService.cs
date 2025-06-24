using Microsoft.Extensions.Caching.Memory;
using XiansAi.Server.Features.WebApi.Repositories;

namespace XiansAi.Server.Features.WebApi.Services
{
    public interface IRoleCacheService
    {
        Task<List<string>> GetUserRolesAsync(string userId, string tenantId);
        void InvalidateUserRoles(string userId, string tenantId);
    }

    public class RoleCacheService : IRoleCacheService
    {
        private readonly IMemoryCache _cache;
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        public RoleCacheService(
            IMemoryCache cache,
            IUserRoleRepository userRoleRepository)
        {
            _cache = cache;
            _userRoleRepository = userRoleRepository;
        }

        public async Task<List<string>> GetUserRolesAsync(string userId, string tenantId)
        {
            var cacheKey = $"{tenantId}:{userId}:roles";
            if (!_cache.TryGetValue(cacheKey, out List<string>? roles))
            {
                var userRoles = await _userRoleRepository.GetUserRolesAsync(userId, tenantId);
                roles = userRoles?.Roles ?? new List<string>();
                _cache.Set(cacheKey, roles, _cacheDuration);
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
