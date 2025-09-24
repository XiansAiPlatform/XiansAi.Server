using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface IInvitationRepository
{
    Task<List<Invitation>> GetAllAsync(string tenantId);
    Task<Invitation?> GetByTokenAsync(string token);
    Task<Invitation?> GetByEmailAsync(string email);
    Task<bool> CreateAsync(Invitation invitation);
    Task<bool> MarkAsAcceptedAsync(string token);
    Task<bool> MarkAsExpiredAsync(string token);
    Task<bool> DeleteInvitation(string token);
}

public class InvitationRepository : IInvitationRepository
{
    private readonly IMongoCollection<Invitation> _collection;

    public InvitationRepository(IDatabaseService db)
    {
        var database = db.GetDatabaseAsync().Result;
        _collection = database.GetCollection<Invitation>("user_invitations");
    }

    public async Task<List<Invitation>> GetAllAsync(string tenantId)
    {
        return await _collection.Find(x => x.TenantId == tenantId).ToListAsync();
    }

    public async Task<Invitation?> GetByTokenAsync(string token)
    {
        return await _collection.Find(x => x.Token == token).FirstOrDefaultAsync();
    }

    public async Task<Invitation?> GetByEmailAsync(string email)
    {
        return await _collection.Find(x => x.Email == email && x.Status != Status.Accepted && x.Status != Status.Expired).FirstOrDefaultAsync();
    }

    public async Task<bool> CreateAsync(Invitation invitation)
    {
        await _collection.InsertOneAsync(invitation);
        return true;
    }

    public async Task<bool> MarkAsAcceptedAsync(string token)
    {
        var update = Builders<Invitation>.Update.Set(x => x.Status, Status.Accepted);
        var result = await _collection.UpdateOneAsync(x => x.Token == token, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> MarkAsExpiredAsync(string token)
    {
        var update = Builders<Invitation>.Update.Set(x => x.Status, Status.Expired);
        var result = await _collection.UpdateOneAsync(x => x.Token == token, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteInvitation(string token)
    {
        var filter = Builders<Invitation>.Filter.Eq(u => u.Token, token);
        var deletedUser = await _collection.DeleteOneAsync(filter);

        return deletedUser.IsAcknowledged;
    }
}