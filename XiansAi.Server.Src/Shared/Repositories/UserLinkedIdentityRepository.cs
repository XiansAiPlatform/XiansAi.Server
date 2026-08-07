using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>
/// Result of attaching a provider identity to an account. An identity resolves to exactly one
/// account, so an attempt to attach one that is spoken for has to be distinguishable from success.
/// </summary>
public enum LinkIdentityOutcome
{
    Added,
    AlreadyLinkedToThisUser,
    TakenByAnotherUser
}

public interface IUserLinkedIdentityRepository
{
    Task<UserLinkedIdentity?> GetAsync(string subject, string authority);
    Task<List<UserLinkedIdentity>> GetForUserAsync(string userId);
    Task<LinkIdentityOutcome> AddAsync(UserLinkedIdentity identity);
    Task<bool> RemoveAsync(string userId, string subject, string authority);
}

/// <summary>
/// Stores the provider identities that resolve to an account other than the one their own subject
/// would create. Uniqueness of an identity across accounts is enforced by the collection's index
/// rather than by reading first, so two concurrent links cannot both succeed.
/// </summary>
public class UserLinkedIdentityRepository : IUserLinkedIdentityRepository
{
    private readonly IMongoCollection<UserLinkedIdentity> _identities;
    private readonly ILogger<UserLinkedIdentityRepository> _logger;

    public UserLinkedIdentityRepository(
        IDatabaseService databaseService,
        ILogger<UserLinkedIdentityRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _identities = database.GetCollection<UserLinkedIdentity>("user_linked_identities");
        _logger = logger;
    }

    /// <summary>
    /// Finds the identity matching a presented token, or null when it is not linked anywhere.
    ///
    /// Both halves of the pair are matched together: a subject is unique only within the issuer that
    /// minted it, so matching them independently would let one provider's subject pair with another
    /// provider's authority and resolve an account neither identity belongs to.
    /// </summary>
    public async Task<UserLinkedIdentity?> GetAsync(string subject, string authority)
    {
        var normalizedAuthority = LinkedIdentityKey.NormalizeAuthority(authority);

        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _identities
                .Find(x => x.Subject == subject && x.Authority == normalizedAuthority)
                .FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetLinkedIdentity");
    }

    public async Task<List<UserLinkedIdentity>> GetForUserAsync(string userId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _identities.Find(x => x.UserId == userId).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetLinkedIdentitiesForUser");
    }

    /// <summary>
    /// Attaches a provider identity to an account. An identity already held by another account is
    /// refused by the unique index, which is what makes the outcome authoritative rather than a
    /// best-effort check that a concurrent link could slip past.
    /// </summary>
    public async Task<LinkIdentityOutcome> AddAsync(UserLinkedIdentity identity)
    {
        identity.Authority = LinkedIdentityKey.NormalizeAuthority(identity.Authority);

        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            try
            {
                await _identities.InsertOneAsync(identity);
                return LinkIdentityOutcome.Added;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                var existing = await _identities
                    .Find(x => x.Subject == identity.Subject && x.Authority == identity.Authority)
                    .FirstOrDefaultAsync();

                if (existing != null && string.Equals(existing.UserId, identity.UserId, StringComparison.Ordinal))
                {
                    return LinkIdentityOutcome.AlreadyLinkedToThisUser;
                }

                _logger.LogWarning(
                    "Cannot link subject to {UserId}: it is already linked to another account",
                    LogSanitizer.RedactUserId(identity.UserId));
                return LinkIdentityOutcome.TakenByAnotherUser;
            }
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "AddLinkedIdentity");
    }

    /// <summary>
    /// Detaches a provider identity from an account, so that a mistaken link can be undone. Scoped to
    /// the account as well as the identity, so an administrator cannot remove another account's link
    /// by naming the wrong user.
    /// </summary>
    public async Task<bool> RemoveAsync(string userId, string subject, string authority)
    {
        var normalizedAuthority = LinkedIdentityKey.NormalizeAuthority(authority);

        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _identities.DeleteOneAsync(
                x => x.UserId == userId && x.Subject == subject && x.Authority == normalizedAuthority);

            return result.DeletedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "RemoveLinkedIdentity");
    }
}
