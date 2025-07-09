using MongoDB.Driver;
using Shared.Data;
using Shared.Utils;
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
            var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
            _collection = database.GetCollection<UserRole>("user_roles");
            _logger = logger;
        }

        public async Task<UserRole?> GetUserRolesAsync(string userId, string tenantId)
        {
            // Sanitize and validate inputs
            var sanitizedUserId = InputSanitizationUtils.SanitizeUserId(userId);
            InputValidationUtils.ValidateUserId(sanitizedUserId, nameof(userId));

            var sanitizedTenantId = InputSanitizationUtils.SanitizeTenantId(tenantId);
            InputValidationUtils.ValidateTenantId(sanitizedTenantId, nameof(tenantId));

            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.UserId, sanitizedUserId),
                Builders<UserRole>.Filter.Eq(x => x.TenantId, sanitizedTenantId)
            );

            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> AssignRolesAsync(string userId, string tenantId, List<string> roles, string createdBy)
        {
            // Sanitize and validate inputs
            var sanitizedUserId = InputSanitizationUtils.SanitizeUserId(userId);
            InputValidationUtils.ValidateUserId(sanitizedUserId, nameof(userId));

            var sanitizedTenantId = InputSanitizationUtils.SanitizeTenantId(tenantId);
            InputValidationUtils.ValidateTenantId(sanitizedTenantId, nameof(tenantId));

            var sanitizedCreatedBy = InputSanitizationUtils.SanitizeCreatedBy(createdBy);
            InputValidationUtils.ValidateCreatedBy(sanitizedCreatedBy, nameof(createdBy));

            if (roles == null || roles.Count == 0)
            {
                throw new ArgumentException("Roles cannot be null or empty", nameof(roles));
            }

            var userRole = new UserRole
            {
                UserId = sanitizedUserId,
                TenantId = sanitizedTenantId,
                Roles = roles,
                CreatedBy = sanitizedCreatedBy,
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
            // Sanitize and validate inputs
            var sanitizedUserId = InputSanitizationUtils.SanitizeUserId(userId);
            InputValidationUtils.ValidateUserId(sanitizedUserId, nameof(userId));

            var sanitizedTenantId = InputSanitizationUtils.SanitizeTenantId(tenantId);
            InputValidationUtils.ValidateTenantId(sanitizedTenantId, nameof(tenantId));

            if (roles == null || roles.Count == 0)
            {
                throw new ArgumentException("Roles cannot be null or empty", nameof(roles));
            }

            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.UserId, sanitizedUserId),
                Builders<UserRole>.Filter.Eq(x => x.TenantId, sanitizedTenantId)
            );

            var update = Builders<UserRole>.Update
                .Set(x => x.Roles, roles)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<List<UserRole>> GetUsersByRoleAsync(string role, string tenantId)
        {
            // Sanitize and validate inputs
            var sanitizedTenantId = InputSanitizationUtils.SanitizeTenantId(tenantId);
            InputValidationUtils.ValidateTenantId(sanitizedTenantId, nameof(tenantId));

            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException("Role cannot be null or empty", nameof(role));
            }

            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.TenantId, sanitizedTenantId),
                Builders<UserRole>.Filter.AnyEq(x => x.Roles, role)
            );

            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<bool> RemoveRoleAsync(string userId, string tenantId, string role)
        {
            // Sanitize and validate inputs
            var sanitizedUserId = InputSanitizationUtils.SanitizeUserId(userId);
            InputValidationUtils.ValidateUserId(sanitizedUserId, nameof(userId));

            var sanitizedTenantId = InputSanitizationUtils.SanitizeTenantId(tenantId);
            InputValidationUtils.ValidateTenantId(sanitizedTenantId, nameof(tenantId));

            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException("Role cannot be null or empty", nameof(role));
            }

            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.UserId, sanitizedUserId),
                Builders<UserRole>.Filter.Eq(x => x.TenantId, sanitizedTenantId)
            );

            var update = Builders<UserRole>.Update.Pull(x => x.Roles, role);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<List<UserRole>> GetAllTenantAdminsAsync(string tenantId)
        {
            // Sanitize and validate inputs
            var sanitizedTenantId = InputSanitizationUtils.SanitizeTenantId(tenantId);
            InputValidationUtils.ValidateTenantId(sanitizedTenantId, nameof(tenantId));

            var filter = Builders<UserRole>.Filter.And(
                Builders<UserRole>.Filter.Eq(x => x.TenantId, sanitizedTenantId),
                Builders<UserRole>.Filter.AnyEq(x => x.Roles, "TenantAdmin")
            );

            return await _collection.Find(filter).ToListAsync();
        }
    }
}
