using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface IContactRepository
{
    Task<List<Contact>> ListAsync(string tenantId, string? search = null);
    Task<Contact?> GetAsync(string tenantId, string id);
    Task<Contact> CreateAsync(Contact contact);
    Task<bool> UpdateAsync(Contact contact);
    Task<bool> DeleteAsync(string tenantId, string id);
    Task<long> DeleteByTenantAsync(string tenantId);
}

/// <summary>
/// Every query filters on <c>tenant_id</c>, so a contact can never be read or changed from another tenant.
/// </summary>
public class ContactRepository : IContactRepository
{
    private readonly IMongoCollection<Contact> _collection;

    public ContactRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _collection = database.GetCollection<Contact>("contacts");
    }

    public async Task<List<Contact>> ListAsync(string tenantId, string? search = null)
    {
        var builder = Builders<Contact>.Filter;
        var filter = builder.Eq(c => c.TenantId, tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
            filter &= builder.Or(builder.Regex(c => c.Name, pattern), builder.Regex(c => c.Email, pattern));
        }

        return await _collection.Find(filter).SortBy(c => c.Name).ToListAsync();
    }

    public async Task<Contact?> GetAsync(string tenantId, string id)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        return await _collection.Find(c => c.TenantId == tenantId && c.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Contact> CreateAsync(Contact contact)
    {
        await _collection.InsertOneAsync(contact);
        return contact;
    }

    public async Task<bool> UpdateAsync(Contact contact)
    {
        var result = await _collection.ReplaceOneAsync(
            c => c.TenantId == contact.TenantId && c.Id == contact.Id, contact);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string tenantId, string id)
    {
        if (!ObjectId.TryParse(id, out _)) return false;
        var result = await _collection.DeleteOneAsync(c => c.TenantId == tenantId && c.Id == id);
        return result.DeletedCount > 0;
    }

    public async Task<long> DeleteByTenantAsync(string tenantId)
    {
        var result = await _collection.DeleteManyAsync(c => c.TenantId == tenantId);
        return result.DeletedCount;
    }
}
