using Features.AdminApi.Models;
using MongoDB.Driver;
using Shared.Data;
using Shared.Repositories;
using Shared.Utils;

namespace Features.AdminApi.Repositories;

/// <summary>
/// Mongo access for the AdminApi capability matrix. 
/// The matrix is a collection of <see cref="CapabilityMatrixEntry"/> documents, 
/// one per action, that declare which roles may perform that action. 
/// The matrix is read by <see cref="CapabilityMatrixFilter"/> to authorize requests, 
/// and written by <see cref="AdminCapabilityMatrixEndpoints"/> to widen or narrow
/// the AdminApi's role-based access control.
/// </summary>
public interface ICapabilityMatrixRepository
{
    Task<List<CapabilityMatrixEntry>> GetAllAsync();

    /// <summary>Writes the row's fields, returning the row as it was before, or null when it created one.</summary>
    Task<CapabilityMatrixEntry?> UpsertAsync(CapabilityMatrixEntry entry);

    /// <summary>Removes the row, returning what was deleted, or null when there was none.</summary>
    Task<CapabilityMatrixEntry?> DeleteAsync(string action);
}

public class CapabilityMatrixRepository : ICapabilityMatrixRepository
{
    private readonly IMongoCollection<CapabilityMatrixEntry> _collection;
    private readonly ILogger<CapabilityMatrixRepository> _logger;

    public CapabilityMatrixRepository(IDatabaseService databaseService, ILogger<CapabilityMatrixRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<CapabilityMatrixEntry>("admin_capability_matrix");
    }

    public async Task<List<CapabilityMatrixEntry>> GetAllAsync()
    {
        try
        {
            return await _collection.Find(Builders<CapabilityMatrixEntry>.Filter.Empty).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing the capability matrix; falling back to code defaults");
            return [];
        }
    }

    public async Task<CapabilityMatrixEntry?> UpsertAsync(CapabilityMatrixEntry entry)
    {
        var update = Builders<CapabilityMatrixEntry>.Update
            .Set(x => x.Action, entry.Action)
            .Set(x => x.AllowedRoles, entry.AllowedRoles)
            .Set(x => x.Description, entry.Description)
            .Set(x => x.UpdatedAt, entry.UpdatedAt)
            .Set(x => x.UpdatedBy, entry.UpdatedBy);

        return await _collection.FindOneAndUpdateAsync<CapabilityMatrixEntry>(
            x => x.Action == entry.Action,
            update,
            new FindOneAndUpdateOptions<CapabilityMatrixEntry>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.Before,
            });
    }

    public async Task<CapabilityMatrixEntry?> DeleteAsync(string action) =>
        await _collection.FindOneAndDeleteAsync(x => x.Action == action);
}
