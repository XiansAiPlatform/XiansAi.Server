using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Providers;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Providers.SecretStore;

public class DatabaseSecretStoreProviderTests : IClassFixture<MongoDbFixture>, IAsyncLifetime
{
    private const string BaseSecret = "unit-test-base-secret-min-32-chars-padding-padding";

    private readonly MongoDbFixture _fixture;
    private readonly DatabaseSecretStoreProvider _provider;
    private readonly SecureEncryptionService _encryption;

    public DatabaseSecretStoreProviderTests(MongoDbFixture fixture)
    {
        _fixture = fixture;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionKeys:BaseSecret"] = BaseSecret,
                ["EncryptionKeys:UniqueSecrets:SecretVaultKey"] = "legacy-only-secret-min-32-chars!!!"
            })
            .Build();
        _encryption = new SecureEncryptionService(NullLogger<SecureEncryptionService>.Instance, configuration);

        var dbService = new TestDatabaseService(fixture.Database);
        _provider = new DatabaseSecretStoreProvider(
            dbService,
            _encryption,
            configuration,
            NullLogger<DatabaseSecretStoreProvider>.Instance);
    }

    public Task InitializeAsync()
    {
        _fixture.Database.DropCollection(DatabaseSecretStoreProvider.ValuesCollectionName);
        _fixture.Database.DropCollection(DatabaseSecretStoreProvider.MetadataCollectionName);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RoundTrip_StoresAndReturnsValue()
    {
        var id = ObjectId.GenerateNewId().ToString();
        await _provider.SetAsync(id, "p@ssw0rd");

        var value = await _provider.GetAsync(id);

        Assert.Equal("p@ssw0rd", value);
    }

    [Fact]
    public async Task SetAsync_OverwritesExisting()
    {
        var id = ObjectId.GenerateNewId().ToString();
        await _provider.SetAsync(id, "first");
        await _provider.SetAsync(id, "second");

        var value = await _provider.GetAsync(id);

        Assert.Equal("second", value);
    }

    [Fact]
    public async Task GetAsync_Returns_Null_When_Missing()
    {
        var id = ObjectId.GenerateNewId().ToString();

        var value = await _provider.GetAsync(id);

        Assert.Null(value);
    }

    [Fact]
    public async Task DeleteAsync_RemovesValue()
    {
        var id = ObjectId.GenerateNewId().ToString();
        await _provider.SetAsync(id, "to-be-deleted");

        await _provider.DeleteAsync(id);

        Assert.Null(await _provider.GetAsync(id));
    }

    [Fact]
    public async Task DeleteAsync_NoOp_For_Missing()
    {
        var id = ObjectId.GenerateNewId().ToString();
        await _provider.DeleteAsync(id);
    }

    [Fact]
    public async Task PerRecordKey_DerivedFromSecretId()
    {
        // Two different ids encrypting the same plaintext must produce different ciphertexts.
        var id1 = ObjectId.GenerateNewId().ToString();
        var id2 = ObjectId.GenerateNewId().ToString();

        await _provider.SetAsync(id1, "same-value");
        await _provider.SetAsync(id2, "same-value");

        var values = _fixture.Database.GetCollection<DatabaseSecretStoreProvider.SecretValueDocument>(
            DatabaseSecretStoreProvider.ValuesCollectionName);

        var doc1 = await values.Find(x => x.Id == id1).FirstAsync();
        var doc2 = await values.Find(x => x.Id == id2).FirstAsync();

        Assert.NotEqual(doc1.Ciphertext, doc2.Ciphertext);
    }

    [Fact]
    public async Task GetAsync_FallsBack_To_Legacy_EncryptedValue()
    {
        // Simulate a row written by the old code path: encrypted_value lives on the metadata document
        // and there is no document in secret_vault_values.
        var id = ObjectId.GenerateNewId().ToString();
        var legacyCiphertext = _encryption.Encrypt("legacy-value", "legacy-only-secret-min-32-chars!!!");

        var metadata = _fixture.Database.GetCollection<BsonDocument>(DatabaseSecretStoreProvider.MetadataCollectionName);
        await metadata.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new ObjectId(id),
            ["key"] = "legacy-key",
            ["encrypted_value"] = legacyCiphertext,
            ["created_at"] = DateTime.UtcNow,
            ["created_by"] = "test"
        });

        var value = await _provider.GetAsync(id);

        Assert.Equal("legacy-value", value);
    }

    private sealed class TestDatabaseService : IDatabaseService
    {
        private readonly IMongoDatabase _database;
        public TestDatabaseService(IMongoDatabase database) { _database = database; }
        public Task<IMongoDatabase> GetDatabaseAsync() => Task.FromResult(_database);
    }
}
