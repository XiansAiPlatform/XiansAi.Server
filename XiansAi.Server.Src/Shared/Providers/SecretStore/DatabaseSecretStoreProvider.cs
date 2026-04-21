using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Shared.Data;
using Shared.Services;

namespace Shared.Providers;

/// <summary>
/// Default Secret Store provider. Persists each secret value as an AES-256-GCM ciphertext
/// in a dedicated MongoDB collection (<c>secret_vault_values</c>), keyed by <c>secretId</c>.
///
/// Design notes:
/// <list type="bullet">
///   <item>Values are stored in a separate collection from <c>secret_vault</c> metadata so that
///   list/get-by-id projections cannot accidentally leak ciphertext.</item>
///   <item>The per-record unique-secret used for key derivation is the <c>secretId</c> itself.
///   This means each row uses a distinct AES key derived from <c>BaseSecret</c> + <c>secretId</c>;
///   compromising one ciphertext does not weaken any other.</item>
///   <item>Backwards compatibility: if no value document is found, the provider falls back to
///   reading the legacy <c>encrypted_value</c> field on the metadata document and decrypting it
///   with the legacy unique-secret (<c>EncryptionKeys:UniqueSecrets:SecretVaultKey</c>, falling
///   back to <c>BaseSecret</c>). This allows in-place upgrade with zero-downtime.</item>
/// </list>
/// </summary>
public class DatabaseSecretStoreProvider : ISecretStoreProvider
{
    public const string ValuesCollectionName = "secret_vault_values";
    public const string MetadataCollectionName = "secret_vault";

    private readonly IMongoCollection<SecretValueDocument> _values;
    private readonly IMongoCollection<BsonDocument> _metadata;
    private readonly ISecureEncryptionService _encryption;
    private readonly ILogger<DatabaseSecretStoreProvider> _logger;
    private readonly string _legacyUniqueSecret;

    public DatabaseSecretStoreProvider(
        IDatabaseService databaseService,
        ISecureEncryptionService encryption,
        IConfiguration configuration,
        ILogger<DatabaseSecretStoreProvider> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _values = database.GetCollection<SecretValueDocument>(ValuesCollectionName);
        _metadata = database.GetCollection<BsonDocument>(MetadataCollectionName);
        _encryption = encryption;
        _logger = logger;

        // Legacy fallback only. New rows derive the per-record unique-secret from the secretId.
        var legacy = configuration["EncryptionKeys:UniqueSecrets:SecretVaultKey"];
        if (string.IsNullOrWhiteSpace(legacy))
        {
            legacy = configuration["EncryptionKeys:BaseSecret"];
        }
        _legacyUniqueSecret = legacy ?? string.Empty;
    }

    public string Name => "database";

    public async Task SetAsync(string secretId, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretId))
            throw new ArgumentException("secretId must be provided", nameof(secretId));
        ArgumentNullException.ThrowIfNull(value);

        var ciphertext = _encryption.Encrypt(value, secretId);
        var now = DateTime.UtcNow;

        var update = Builders<SecretValueDocument>.Update
            .Set(x => x.Ciphertext, ciphertext)
            .Set(x => x.UpdatedAt, now)
            .SetOnInsert(x => x.Id, secretId)
            .SetOnInsert(x => x.CreatedAt, now);

        await _values.UpdateOneAsync(
            x => x.Id == secretId,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<string?> GetAsync(string secretId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretId))
            throw new ArgumentException("secretId must be provided", nameof(secretId));

        var doc = await _values.Find(x => x.Id == secretId).FirstOrDefaultAsync(cancellationToken);
        if (doc != null)
        {
            return _encryption.Decrypt(doc.Ciphertext, secretId);
        }

        // Backwards-compatible fallback: legacy encrypted_value lives on the metadata document.
        try
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(secretId));
            var legacy = await _metadata
                .Find(filter)
                .Project(Builders<BsonDocument>.Projection.Include("encrypted_value"))
                .FirstOrDefaultAsync(cancellationToken);

            if (legacy != null && legacy.TryGetValue("encrypted_value", out var encValue) && encValue.IsString)
            {
                var ciphertext = encValue.AsString;
                if (string.IsNullOrEmpty(ciphertext))
                    return null;

                if (string.IsNullOrEmpty(_legacyUniqueSecret))
                {
                    _logger.LogError(
                        "Legacy encrypted_value found for secret {SecretId} but no legacy SecretVaultKey configured",
                        secretId);
                    return null;
                }

                return _encryption.Decrypt(ciphertext, _legacyUniqueSecret);
            }
        }
        catch (FormatException)
        {
            // secretId is not a valid ObjectId; nothing to fall back to.
        }

        return null;
    }

    public async Task DeleteAsync(string secretId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretId))
            throw new ArgumentException("secretId must be provided", nameof(secretId));

        await _values.DeleteOneAsync(x => x.Id == secretId, cancellationToken);
    }

    /// <summary>
    /// MongoDB document for a single secret value. Stored in <see cref="ValuesCollectionName"/>.
    /// The <c>_id</c> is the same as the metadata document <c>_id</c>.
    /// </summary>
    public class SecretValueDocument
    {
        [BsonId]
        public string Id { get; set; } = null!;

        [BsonElement("ciphertext")]
        public string Ciphertext { get; set; } = null!;

        [BsonElement("created_at")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
