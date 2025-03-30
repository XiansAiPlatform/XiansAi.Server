using MongoDB.Driver;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XiansAi.Server.Auth.Models;
using XiansAi.Server.Database;

namespace XiansAi.Server.Auth.Repositories
{
    public class PermissionRepository : IPermissionRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ITenantContext _tenantContext;

        public PermissionRepository(IDatabaseService databaseService, ITenantContext tenantContext)
        {
            _databaseService = databaseService;
            _tenantContext = tenantContext;
        }

        public async Task<PermissionDocument?> GetEntityPermissionAsync(string entityId, string entityType, string tenantId)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<PermissionDocument>("permissions");

            return await collection.Find(p => 
                p.EntityId == entityId && 
                p.EntityType == entityType && 
                p.TenantId == tenantId)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> SetPermissionAsync(string entityId, string entityType, string tenantId, string userId, string permissionLevel)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<PermissionDocument>("permissions");

            var permission = await GetEntityPermissionAsync(entityId, entityType, tenantId);

            if (permission == null)
            {
                // Create new permission document
                permission = new PermissionDocument
                {
                    EntityId = entityId,
                    EntityType = entityType,
                    TenantId = tenantId,
                    Permissions = new List<UserPermission>
                    {
                        new UserPermission
                        {
                            UserId = userId,
                            Level = permissionLevel
                        }
                    }
                };

                await collection.InsertOneAsync(permission);
                return true;
            }
            else
            {
                // Update existing permission
                var existingPermission = permission.Permissions.FirstOrDefault(p => p.UserId == userId);
                
                if (existingPermission != null)
                {
                    // Update permission level
                    existingPermission.Level = permissionLevel;
                }
                else
                {
                    // Add new permission
                    permission.Permissions.Add(new UserPermission
                    {
                        UserId = userId,
                        Level = permissionLevel
                    });
                }

                var result = await collection.ReplaceOneAsync(
                    p => p.EntityId == entityId && p.EntityType == entityType && p.TenantId == tenantId,
                    permission);

                return result.ModifiedCount > 0;
            }
        }

        public async Task<bool> RemovePermissionAsync(string entityId, string entityType, string tenantId, string userId)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<PermissionDocument>("permissions");

            var permission = await GetEntityPermissionAsync(entityId, entityType, tenantId);

            if (permission == null)
            {
                return false;
            }

            permission.Permissions = permission.Permissions
                .Where(p => p.UserId != userId)
                .ToList();

            var result = await collection.ReplaceOneAsync(
                p => p.EntityId == entityId && p.EntityType == entityType && p.TenantId == tenantId,
                permission);

            return result.ModifiedCount > 0;
        }

        public async Task<IEnumerable<PermissionDocument>> GetUserPermissionsAsync(string userId, string tenantId)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<PermissionDocument>("permissions");

            return await collection.Find(p => 
                p.TenantId == tenantId && 
                p.Permissions.Any(up => up.UserId == userId))
                .ToListAsync();
        }
    }
} 