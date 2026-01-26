using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils;

namespace Shared.Repositories;

public interface IUserRepository
{
    Task<PagedUserResult> GetAllUsersAsync(UserFilter filter);
    Task<PagedUserResult> GetAllUsersByTenantAsync(UserFilter filter);
    Task<List<User>> GetSystemAdminAsync();
    Task<List<User>> GetUsersWithUnapprovedTenantAsync(string? tenantId = null);
    Task<List<User>> GetUsersByRoleAsync(string roleName, string tenantId);
    Task<User?> GetByUserIdAsync(string userId);
    Task<User?> GetByIdAsync(string id);
    Task<User?> GetByUserEmailAsync(string email);
    Task<List<string>> GetUserTenantsAsync(string userId);
    Task<List<string>> GetUserRolesAsync(string userId, string tenantId);
    Task<User?> GetAnyUserAsync();
    Task<bool> CreateAsync(User user);
    Task<bool> UpdateAsync(string userId, User user);
    Task<bool> UpdateAsyncById(string id, User user);
    Task<bool> LockUserAsync(string userId, string reason, string lockedByUserId);
    Task<bool> UnlockUserAsync(string userId);
    Task<bool> IsLockedOutAsync(string userId);
    Task<bool> IsSysAdmin(string userId);
    Task<bool> DeleteUser(string userId, string? tenantId = null);
    Task<List<User>> SearchUsersAsync(string query, string? tenantId = null);
}

public class UserRepository : IUserRepository
{
    private readonly IMongoCollection<User> _users;
    private readonly ILogger<UserRepository> _logger;
    private readonly ITenantRepository _tenantRepository;

    public UserRepository(IDatabaseService databaseService, ILogger<UserRepository> logger, ITenantRepository tenantRepository)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _users = database.GetCollection<User>("users");
        _logger = logger;
        _tenantRepository = tenantRepository;
    }

    public async Task<PagedUserResult> GetAllUsersAsync(UserFilter filter)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var builder = Builders<User>.Filter;
            var filters = new List<FilterDefinition<User>>();

            // Filter by user type
            switch (filter.Type)
            {
                case UserTypeFilter.ADMIN:
                    filters.Add(builder.Eq(u => u.IsSysAdmin, true));
                    break;
                case UserTypeFilter.NON_ADMIN:
                    filters.Add(builder.Eq(u => u.IsSysAdmin, false));
                    break;
                case UserTypeFilter.ALL:
                default:
                    // No additional filter
                    break;
            }

            // Filter by tenant
            if (!string.IsNullOrWhiteSpace(filter.Tenant))
            {
                filters.Add(builder.ElemMatch(u => u.TenantRoles, tr => tr.Tenant == filter.Tenant));
            }

            // Search by name or email (case-insensitive, partial match)
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search.Trim();
                var nameFilter = builder.Regex(u => u.Name, new MongoDB.Bson.BsonRegularExpression(search, "i"));
                var emailFilter = builder.Regex(u => u.Email, new MongoDB.Bson.BsonRegularExpression(search, "i"));
                filters.Add(builder.Or(nameFilter, emailFilter));
            }

            var mongoFilter = filters.Count > 0 ? builder.And(filters) : builder.Empty;

            // Paging
            int page = filter.Page > 0 ? filter.Page : 1;
            int pageSize = filter.PageSize > 0 ? filter.PageSize : 20;
            int skip = (page - 1) * pageSize;

            var users = await _users
                .Find(mongoFilter)
                .Skip(skip)
                .Limit(pageSize)
                .ToListAsync();

            var totalCount = await _users.CountDocumentsAsync(mongoFilter);

            return new PagedUserResult
            {
                Users = users,
                TotalCount = totalCount,
            };
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAllUsers");
    }

    public async Task<PagedUserResult> GetAllUsersByTenantAsync(UserFilter filter)
    {
        var builder = Builders<User>.Filter;
        var filters = new List<FilterDefinition<User>>();

        // Must filter by tenant for role-based filtering
        if (string.IsNullOrWhiteSpace(filter.Tenant))
            throw new ArgumentException("Tenant is required for role-based user filtering.");

        switch (filter.Type)
        {
            case UserTypeFilter.ADMIN:
                filters.Add(builder.ElemMatch(u => u.TenantRoles,
                    tr => tr.Tenant == filter.Tenant && tr.Roles.Contains("TenantAdmin") && tr.IsApproved));
                break;

            case UserTypeFilter.NON_ADMIN:
                filters.Add(builder.ElemMatch(u => u.TenantRoles,
                    tr => tr.Tenant == filter.Tenant && tr.Roles.Contains("TenantUser") && tr.IsApproved));
                break;

            case UserTypeFilter.PARTICIPANT:
                filters.Add(builder.ElemMatch(u => u.TenantRoles,
                    tr => tr.Tenant == filter.Tenant && tr.Roles.Contains("TenantParticipant") && tr.IsApproved));
                break;

            case UserTypeFilter.ALL:
            default:
                filters.Add(builder.ElemMatch(u => u.TenantRoles,
                    tr => tr.Tenant == filter.Tenant && tr.IsApproved));
                break;
        }

        // Search by name or email (case-insensitive, partial match)
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            var nameFilter = builder.Regex(u => u.Name, new MongoDB.Bson.BsonRegularExpression(search, "i"));
            var emailFilter = builder.Regex(u => u.Email, new MongoDB.Bson.BsonRegularExpression(search, "i"));
            filters.Add(builder.Or(nameFilter, emailFilter));
        }

        var mongoFilter = builder.And(filters);

        // Paging
        int page = filter.Page > 0 ? filter.Page : 1;
        int pageSize = filter.PageSize > 0 ? filter.PageSize : 20;
        int skip = (page - 1) * pageSize;

        var users = await _users
            .Find(mongoFilter)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync();

        var totalCount = await _users.CountDocumentsAsync(mongoFilter);

        return new PagedUserResult
        {
            Users = users,
            TotalCount = totalCount,
        };
    }


    public async Task<List<User>> GetSystemAdminAsync()
    {
        return await _users.Find(x => x.IsSysAdmin == true).ToListAsync();
    }

    public async Task<List<User>> GetUsersWithUnapprovedTenantAsync(string? tenantId = null)
    {
        FilterDefinition<User> filter;

        if (tenantId == null)
        {
            filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Or(
                Builders<User>.Filter.Eq(u => u.TenantRoles, null),
                Builders<User>.Filter.Size(u => u.TenantRoles, 0),
                Builders<User>.Filter.ElemMatch(
                u => u.TenantRoles,
                tr => tr.IsApproved == false
                )
            ),
            Builders<User>.Filter.Eq(u => u.IsSysAdmin, false)
        );
        }
        else
        {
            filter = Builders<User>.Filter.And(
            Builders<User>.Filter.ElemMatch(
                u => u.TenantRoles,
                tr => tr.Tenant == tenantId && tr.IsApproved == false
                )
            );
        }

        return await _users.Find(filter).ToListAsync();
    }

    public async Task<List<User>> GetUsersByRoleAsync(string roleName, string tenantId)
    {
        if (roleName == SystemRoles.SysAdmin)
        {
            return await _users.Find(u => u.IsSysAdmin).ToListAsync();
        }

        var filter = Builders<User>.Filter.ElemMatch(
            u => u.TenantRoles,
            tr => tr.Tenant == tenantId && tr.Roles.Contains(roleName)
        );

        return await _users.Find(filter).ToListAsync();
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _users.Find(x => x.Id == id).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetUserById");
    }


    public async Task<User?> GetByUserIdAsync(string userId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _users.Find(x => x.UserId == userId).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetByUserId");
    }

    public async Task<User?> GetByUserEmailAsync(string email)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetByUserEmail");
    }

    public async Task<List<string>> GetUserTenantsAsync(string userId)
    {
        var filter = Builders<User>.Filter.Eq(u => u.UserId, userId);
        var projection = Builders<User>.Projection.Expression(u => u.TenantRoles);

        var tenantRoles = await _users
            .Find(filter)
            .Project(projection)
            .FirstOrDefaultAsync();

        if (tenantRoles == null || !tenantRoles.Any())
        {
            return new List<string>();
        }

        // Get approved tenant IDs from user's tenant roles
        var approvedTenantIds = tenantRoles
            .Where(x => x.IsApproved)
            .Select(tr => tr.Tenant)
            .ToList();

        if (!approvedTenantIds.Any())
        {
            return new List<string>();
        }

        // Validate that each tenant exists and is enabled
        var validTenantIds = new List<string>();
        foreach (var tenantId in approvedTenantIds)
        {
            try
            {
                var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
                if (tenant != null && tenant.Enabled)
                {
                    validTenantIds.Add(tenantId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking tenant {TenantId} for user {UserId}", tenantId, userId);
            }
        }

        return validTenantIds;
    }

    public async Task<User?> GetAnyUserAsync()
    {
        return await _users.Find(_ => true).FirstOrDefaultAsync();
    }

    public async Task<List<string>> GetUserRolesAsync(string userId, string tenantId)
    {
        var filter = Builders<User>.Filter.Eq(u => u.UserId, userId);

        var user = await _users
            .Find(filter)
            .FirstOrDefaultAsync();

        if (user == null)
            return new List<string>();

        // Only return roles for approved tenants
        var tenantRole = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId && tr.IsApproved);
        var result = tenantRole?.Roles ?? new List<string>();

        if (user.IsSysAdmin)
        {
            result.Add(SystemRoles.SysAdmin);
        }

        return result;
    }


    public async Task<bool> CreateAsync(User user)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            try
            {
                await _users.InsertOneAsync(user);
                return true;
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogWarning(ex, "User {UserId} already exists - duplicate key error", user.UserId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user {UserId}", user.UserId);
                return false;
            }
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateUser");
    }

    public async Task<bool> UpdateAsyncById(string id, User user)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            user.UpdatedAt = DateTime.UtcNow;
            var result = await _users.ReplaceOneAsync(x => x.Id == id, user);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateUserById");
    }

    public async Task<bool> UpdateAsync(string userId, User user)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            user.UpdatedAt = DateTime.UtcNow;
            var result = await _users.ReplaceOneAsync(x => x.UserId == userId, user);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateUser");
    }

    public async Task<bool> LockUserAsync(string userId, string reason, string lockedByUserId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var update = Builders<User>.Update
                .Set(x => x.IsLockedOut, true)
                .Set(x => x.LockedOutReason, reason)
                .Set(x => x.LockedOutAt, DateTime.UtcNow)
                .Set(x => x.LockedOutBy, lockedByUserId);

            var result = await _users.UpdateOneAsync(x => x.UserId == userId, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "LockUser");
    }

    public async Task<bool> UnlockUserAsync(string userId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var update = Builders<User>.Update
                .Set(x => x.IsLockedOut, false)
                .Set(x => x.LockedOutReason, null)
                .Set(x => x.LockedOutAt, null)
                .Set(x => x.LockedOutBy, null);

            var result = await _users.UpdateOneAsync(x => x.UserId == userId, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UnlockUser");
    }

    public async Task<bool> IsLockedOutAsync(string userId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var user = await _users.Find(x => x.UserId == userId)
                .Project(x => new { x.IsLockedOut })
                .FirstOrDefaultAsync();
            return user?.IsLockedOut ?? false;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "IsLockedOut");
    }

    public async Task<bool> IsSysAdmin(string userId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var user = await _users.Find(x => x.UserId == userId)
                .Project(x => new { x.IsSysAdmin })
                .FirstOrDefaultAsync();
            return user?.IsSysAdmin ?? false;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "IsSysAdmin");
    }

    public async Task<bool> DeleteUser(string userId, string? tenantId = null)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // First verify the user exists
            var userFilter = Builders<User>.Filter.Eq(u => u.UserId, userId);
            var user = await _users.Find(userFilter).FirstOrDefaultAsync();
            
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                return false;
            }

            // Check if user belongs to the specified tenant (skip check if tenantId is null - SysAdmin action)
            if (tenantId != null)
            {
                var belongsToTenant = user.TenantRoles.Any(tr => tr.Tenant == tenantId);
                if (!belongsToTenant)
                {
                    _logger.LogWarning("User {UserId} does not belong to tenant {TenantId}. IDOR attempt detected.", userId, tenantId);
                    return false;
                }
            }

            // Delete the user
            var deletedUser = await _users.DeleteOneAsync(userFilter);
            return deletedUser.IsAcknowledged && deletedUser.DeletedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteUser");
    }

    public async Task<List<User>> SearchUsersAsync(string query, string? tenantId = null)
    {
        // Search users by email or name
        var searchFilter = Builders<User>.Filter.Or(
            Builders<User>.Filter.Regex(u => u.Email, new BsonRegularExpression(query, "i")),
            Builders<User>.Filter.Regex(u => u.Name, new BsonRegularExpression(query, "i"))
        );

        // If tenantId is provided, filter by tenant (otherwise search all users - SysAdmin action)
        FilterDefinition<User> combinedFilter;
        if (tenantId != null)
        {
            var tenantFilter = Builders<User>.Filter.ElemMatch(
                u => u.TenantRoles,
                Builders<TenantRole>.Filter.Eq(tr => tr.Tenant, tenantId)
            );
            combinedFilter = Builders<User>.Filter.And(searchFilter, tenantFilter);
        }
        else
        {
            combinedFilter = searchFilter;
        }

        return await _users.Find(combinedFilter).Limit(20).ToListAsync();
    }
}