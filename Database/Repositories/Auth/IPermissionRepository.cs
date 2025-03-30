using XiansAi.Server.Auth.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XiansAi.Server.Auth.Repositories
{
    public interface IPermissionRepository
    {
        Task<PermissionDocument?> GetEntityPermissionAsync(string entityId, string entityType, string tenantId);
        Task<bool> SetPermissionAsync(string entityId, string entityType, string tenantId, string userId, string permissionLevel);
        Task<bool> RemovePermissionAsync(string entityId, string entityType, string tenantId, string userId);
        Task<IEnumerable<PermissionDocument>> GetUserPermissionsAsync(string userId, string tenantId);
    }
} 