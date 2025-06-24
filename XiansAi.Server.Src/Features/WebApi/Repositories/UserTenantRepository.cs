using MongoDB.Driver;
using Shared.Data;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Services;

namespace XiansAi.Server.Features.WebApi.Repositories;

public interface IUserTenantRepository
{
    Task<List<string>> GetTenantsForUserAsync(string userId);
    Task<bool> AddTenantToUserAsync(string userId, string tenantId);
    Task<bool> RemoveTenantFromUserAsync(string userId, string tenantId);
}

public class UserTenantRepository : IUserTenantRepository
{
    private readonly IMongoCollection<UserTenant> _collection;

    public UserTenantRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabase().Result;
        _collection = database.GetCollection<UserTenant>("user_tenants");
    }

    public async Task<List<string>> GetTenantsForUserAsync(string userId)
    {
        var filter = Builders<UserTenant>.Filter.Eq(x => x.UserId, userId);
        var userTenants = await _collection.Find(filter).ToListAsync();
        return userTenants.Select(x => x.TenantId).ToList();
    }

    public async Task<bool> AddTenantToUserAsync(string userId, string tenantId)
    {
        var exists = await _collection.Find(x => x.UserId == userId && x.TenantId == tenantId).AnyAsync();
        if (exists) return false;

        var newUserTenant = new UserTenant
        {
            UserId = userId,
            TenantId = tenantId
        };

        await _collection.InsertOneAsync(newUserTenant);
        return true;
    }

    public async Task<bool> RemoveTenantFromUserAsync(string userId, string tenantId)
    {
        var result = await _collection.DeleteOneAsync(x => x.UserId == userId && x.TenantId == tenantId);
        return result.DeletedCount > 0;
    }
}