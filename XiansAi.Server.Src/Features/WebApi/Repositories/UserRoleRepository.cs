using MongoDB.Driver;
using Shared.Data;
using XiansAi.Server.Features.WebApi.Models;

namespace XiansAi.Server.Features.WebApi.Repositories
{
    public interface IUserRoleRepository
    {
        Task<UserRole?> GetUserRolesAsync(string userId, string tenantId);
        Task<bool> AssignRolesAsync(string userId, string tenantId, List<string> roles, string createdBy);
        Task<bool> UpdateRolesAsync(string userId, string tenantId, List<string> roles);
        Task<List<UserRole>> GetUsersByRoleAsync(string role, string tenantId);
        Task<bool> RemoveRoleAsync(string userId, string tenantId, string role);
        Task<List<UserRole>> GetAllTenantAdminsAsync(string tenantId);
    }

    public class UserRoleRepository : IUserRoleRepository
    {
        private readonly IMongoCollection<UserRole> _collection;
        private readonly ILogger<UserRoleRepository> _logger;

        public UserRoleRepository(
            IDatabaseService databaseService,
            ILogger<UserRoleRepository> logger)
        {
            var database = databaseService.GetDatabase().GetAwaiter().GetResult();
            _collection = database.GetCollection<UserRole>("user_roles");
            _logger = logger;

            CreateIndexes();
        }

        private void CreateIndexes()
        {
            var userTenantIndex = Builders<UserRole>.IndexKeys
                .Ascending(x => x.UserId)
                .Ascending(x => x.TenantId);

            var userTenantIndexModel = new CreateIndexModel<UserRole>(
                userTenantIndex,
                new CreateIndexOptions { Unique = true, Background = true }
            );

            _collection.Indexes.CreateMany(new[] { userTenantIndexModel });
        }

        public async Task<UserRole?> GetUserRolesAsync(string userId, string tenantId)
        {
            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.UserId, userId),
                Builders<UserRole>.Filter.Eq(x => x.TenantId, tenantId)
            );

            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> AssignRolesAsync(string userId, string tenantId, List<string> roles, string createdBy)
        {
            var userRole = new UserRole
            {
                UserId = userId,
                TenantId = tenantId,
                Roles = roles,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                await _collection.InsertOneAsync(userRole);
                return true;
            }
            catch (MongoWriteException ex)
            {
                _logger.LogError(ex, "Error assigning roles");
                return false;
            }
        }

        public async Task<bool> UpdateRolesAsync(string userId, string tenantId, List<string> roles)
        {
            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.UserId, userId),
                Builders<UserRole>.Filter.Eq(x => x.TenantId, tenantId)
            );

            var update = Builders<UserRole>.Update
                .Set(x => x.Roles, roles)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<List<UserRole>> GetUsersByRoleAsync(string role, string tenantId)
        {
            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<UserRole>.Filter.AnyEq(x => x.Roles, role)
            );

            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<bool> RemoveRoleAsync(string userId, string tenantId, string role)
        {
            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.UserId, userId),
                Builders<UserRole>.Filter.Eq(x => x.TenantId, tenantId)
            );

            var update = Builders<UserRole>.Update.Pull(x => x.Roles, role);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<List<UserRole>> GetAllTenantAdminsAsync(string tenantId)
        {
            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<UserRole>.Filter.AnyEq(x => x.Roles, "TenantAdmin")
            );

            return await _collection.Find(filter).ToListAsync();
        }
    }
}
