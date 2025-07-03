using MongoDB.Driver;
using Shared.Data;
using XiansAi.Server.Features.WebApi.Models;

namespace XiansAi.Server.Features.WebApi.Repositories;

public interface IUserRepository
{
    Task<List<User>> GetSystemAdminAsync();
    Task<List<User>> GetUsersByRoleAsync(string roleName, string tenantId);
    Task<User?> GetByUserIdAsync(string userId);
    Task<List<string>> GetUserTenantsAsync(string userId);
    Task<List<string>> GetUserRolesAsync(string userId, string tenantId);
    Task<User?> GetAnyUserAsync();
    Task<bool> CreateAsync(User user);
    Task<bool> UpdateAsync(string userId, User user);
    Task<bool> LockUserAsync(string userId, string reason, string lockedByUserId);
    Task<bool> UnlockUserAsync(string userId);
    Task<bool> IsLockedOutAsync(string userId);
}

public class UserRepository : IUserRepository
{
    private readonly IMongoCollection<User> _users;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IDatabaseService databaseService, ILogger<UserRepository> logger)
    {
        var database = databaseService.GetDatabase().Result;
        _users = database.GetCollection<User>("users");
        _logger = logger;
    }

    public async Task<List<User>> GetSystemAdminAsync()
    {
        return await _users.Find(x => x.IsSysAdmin == true).ToListAsync();
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


    public async Task<User?> GetByUserIdAsync(string userId)
    {
        return await _users.Find(x => x.UserId == userId).FirstOrDefaultAsync();
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user {UserId}", user.UserId);
            return false;
        }
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
}