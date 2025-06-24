using Microsoft.Extensions.Caching.Memory;
using Shared.Auth;

namespace XiansAi.Server.Features.WebApi.Services
{
    public interface IUserTenantCacheService
    {
        Task<List<string>> GetUserTenantAsync(string userId);
        void InvalidateUserTenant(string userId, string tenantId);
    }

    public class UserTenantCacheService : IUserTenantCacheService
    {
        private readonly IUserTenantService _userTenantService;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        public UserTenantCacheService(IMemoryCache cache, IUserTenantService userTenantService)
        {
            _cache = cache;
            _userTenantService = userTenantService;
        }
        public async Task<List<string>> GetUserTenantAsync(string userId)
        {
            var cacheKey = $"{userId}:tenants";
            if (!_cache.TryGetValue(cacheKey, out List<string>? tenants))
            {
                var userTenants = await _userTenantService.GetTenantsForUser(userId);
                tenants = userTenants?.Data ?? new List<string>();
                _cache.Set(cacheKey, tenants, _cacheDuration);
            }
            return tenants ?? new List<string>();
        }
        public void InvalidateUserTenant(string userId, string tenantId)
        {
            var cacheKey = $"{tenantId}:{userId}:tenants";
            _cache.Remove(cacheKey);
        }
    }
}
