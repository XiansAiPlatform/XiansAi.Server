using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Services;

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
    Task<bool> DeleteUser(string userId);
    Task<List<User>> SearchUsersAsync(string query);
}

public class UserRepository : IUserRepository
{
    private readonly IMongoCollection<User> _users;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IDatabaseService databaseService, ILogger<UserRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _users = database.GetCollection<User>("users");
        _logger = logger;
    }

    public async Task<PagedUserResult> GetAllUsersAsync(UserFilter filter)
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
        return await _users.Find(x => x.Id == id).FirstOrDefaultAsync();
    }


    public async Task<User?> GetByUserIdAsync(string userId)
    {
        return await _users.Find(x => x.UserId == userId).FirstOrDefaultAsync();
    }

    public async Task<User?> GetByUserEmailAsync(string email)
    {
        return await _users.Find(x => x.Email == email).FirstOrDefaultAsync();
    }

    public async Task<List<string>> GetUserTenantsAsync(string userId)
    {
        var filter = Builders<User>.Filter.Eq(u => u.UserId, userId);
        var projection = Builders<User>.Projection.Expression(u => u.TenantRoles);

        var tenantRoles = await _users
            .Find(filter)
            .Project(projection)
            .FirstOrDefaultAsync();

        return tenantRoles?.Select(tr => tr.Tenant).ToList() ?? new List<string>();
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

        var roles = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId)?.Roles;
        var result = roles ?? new List<string>();


        if (user.IsSysAdmin)
        {
            result.Add(SystemRoles.SysAdmin);
        }

        return result;
    }


    public async Task<bool> CreateAsync(User user)
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
    }

    public async Task<bool> UpdateAsyncById(string id, User user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _users.ReplaceOneAsync(x => x.Id == id, user);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateAsync(string userId, User user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _users.ReplaceOneAsync(x => x.UserId == userId, user);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> LockUserAsync(string userId, string reason, string lockedByUserId)
    {
        var update = Builders<User>.Update
            .Set(x => x.IsLockedOut, true)
            .Set(x => x.LockedOutReason, reason)
            .Set(x => x.LockedOutAt, DateTime.UtcNow)
            .Set(x => x.LockedOutBy, lockedByUserId);

        var result = await _users.UpdateOneAsync(x => x.UserId == userId, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UnlockUserAsync(string userId)
    {
        var update = Builders<User>.Update
            .Set(x => x.IsLockedOut, false)
            .Set(x => x.LockedOutReason, null)
            .Set(x => x.LockedOutAt, null)
            .Set(x => x.LockedOutBy, null);

        var result = await _users.UpdateOneAsync(x => x.UserId == userId, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> IsLockedOutAsync(string userId)
    {
        var user = await _users.Find(x => x.UserId == userId)
            .Project(x => new { x.IsLockedOut })
            .FirstOrDefaultAsync();
        return user?.IsLockedOut ?? false;
    }

    public async Task<bool> IsSysAdmin(string userId)
    {
        var user = await _users.Find(x => x.UserId == userId)
            .Project(x => new { x.IsSysAdmin })
            .FirstOrDefaultAsync();
        return user?.IsSysAdmin ?? false;
    }

    public async Task<bool> DeleteUser(string userId)
    {
        var filter = Builders<User>.Filter.Eq(u => u.UserId, userId);
        var deletedUser = await _users.DeleteOneAsync(filter);

        return deletedUser.IsAcknowledged;
    }

    public async Task<List<User>> SearchUsersAsync(string query)
    {
        var filter = Builders<User>.Filter.Or(
            Builders<User>.Filter.Regex(u => u.Email, new BsonRegularExpression(query, "i")),
            Builders<User>.Filter.Regex(u => u.Name, new BsonRegularExpression(query, "i"))
        );

        return await _users.Find(filter).Limit(20).ToListAsync();
    }
}