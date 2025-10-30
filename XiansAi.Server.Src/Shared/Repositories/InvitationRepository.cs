using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

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
    private readonly ILogger<InvitationRepository> _logger;

    public InvitationRepository(IDatabaseService db, ILogger<InvitationRepository> logger)
    {
        var database = db.GetDatabaseAsync().Result;
        _collection = database.GetCollection<Invitation>("user_invitations");
        _logger = logger;
    }

    public async Task<List<Invitation>> GetAllAsync(string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(x => x.TenantId == tenantId).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAllInvitations");
    }

    public async Task<Invitation?> GetByTokenAsync(string token)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(x => x.Token == token).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetInvitationByToken");
    }

    public async Task<Invitation?> GetByEmailAsync(string email)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(x => x.Email == email && x.Status != Status.Accepted && x.Status != Status.Expired).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetInvitationByEmail");
    }

    public async Task<bool> CreateAsync(Invitation invitation)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _collection.InsertOneAsync(invitation);
            return true;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateInvitation");
    }

    public async Task<bool> MarkAsAcceptedAsync(string token)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var update = Builders<Invitation>.Update.Set(x => x.Status, Status.Accepted);
            var result = await _collection.UpdateOneAsync(x => x.Token == token, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "MarkInvitationAccepted");
    }

    public async Task<bool> MarkAsExpiredAsync(string token)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var update = Builders<Invitation>.Update.Set(x => x.Status, Status.Expired);
            var result = await _collection.UpdateOneAsync(x => x.Token == token, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "MarkInvitationExpired");
    }

    public async Task<bool> DeleteInvitation(string token)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Invitation>.Filter.Eq(u => u.Token, token);
            var deletedUser = await _collection.DeleteOneAsync(filter);
            return deletedUser.IsAcknowledged;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteInvitation");
    }
}