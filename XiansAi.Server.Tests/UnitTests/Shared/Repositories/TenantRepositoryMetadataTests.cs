using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Shared.Data;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Repositories;

/// <summary>
/// DB-level tests for tenant metadata persistence: real MongoDB (ephemeral fixture),
/// real TenantRepository. Verifies BSON round-trips and backward compatibility with
/// documents that predate the metadata field.
/// </summary>
public class TenantRepositoryMetadataTests : IClassFixture<MongoDbFixture>
{
    private readonly IMongoCollection<Tenant> _collection;
    private readonly IMongoCollection<BsonDocument> _rawCollection;
    private readonly TenantRepository _repository;

    public TenantRepositoryMetadataTests(MongoDbFixture fixture)
    {
        _collection = fixture.Database.GetCollection<Tenant>("tenants");
        _rawCollection = fixture.Database.GetCollection<BsonDocument>("tenants");

        var clientService = new Mock<IMongoDbClientService>();
        clientService.Setup(x => x.GetCollection<Tenant>("tenants")).Returns(_collection);
        _repository = new TenantRepository(clientService.Object, NullLogger<TenantRepository>.Instance);
    }

    private static Tenant NewTenant(string tenantId, List<TenantMetadata>? metadata = null)
    {
        return new Tenant
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            Name = $"Tenant {tenantId}",
            Domain = $"{tenantId}.test.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "db-test",
            Metadata = metadata
        };
    }

    [Fact]
    public async Task CreateAsync_PersistsMetadata_AndRoundTripsThroughGetByTenantId()
    {
        var tenantId = $"db-meta-{Guid.NewGuid()}";
        var tenant = NewTenant(tenantId, new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "base64-ciphertext==", Type = MetadataType.Secret },
            new() { Key = "Region", Value = "WestEurope", Type = MetadataType.PlainText }
        });

        await _repository.CreateAsync(tenant);
        var fetched = await _repository.GetByTenantIdAsync(tenantId);

        Assert.NotNull(fetched?.Metadata);
        Assert.Equal(2, fetched!.Metadata!.Count);
        var secret = fetched.Metadata!.Single(m => m.Key == "OpenAiKey");
        Assert.Equal("base64-ciphertext==", secret.Value);
        Assert.Equal(MetadataType.Secret, secret.Type);
        Assert.Equal(MetadataType.PlainText, fetched.Metadata!.Single(m => m.Key == "Region").Type);
    }

    [Fact]
    public async Task Metadata_IsStoredWithSnakeCaseFields_AndTypeAsString()
    {
        var tenantId = $"db-meta-{Guid.NewGuid()}";
        await _repository.CreateAsync(NewTenant(tenantId, new List<TenantMetadata>
        {
            new() { Key = "OpenAiKey", Value = "cipher", Type = MetadataType.Secret }
        }));

        var raw = await _rawCollection.Find(new BsonDocument("tenant_id", tenantId)).FirstAsync();
        var entry = raw["metadata"].AsBsonArray[0].AsBsonDocument;

        Assert.Equal("OpenAiKey", entry["key"].AsString);
        Assert.Equal("cipher", entry["value"].AsString);
        // Enum must be stored as a readable string, not an int, for forward compatibility
        Assert.Equal(BsonType.String, entry["type"].BsonType);
        Assert.Equal("Secret", entry["type"].AsString);
    }

    [Fact]
    public async Task LegacyDocument_WithoutMetadataField_DeserializesWithNullMetadata()
    {
        // Simulates a tenant document created before the metadata feature existed.
        var tenantId = $"db-legacy-{Guid.NewGuid()}";
        await _rawCollection.InsertOneAsync(new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["tenant_id"] = tenantId,
            ["name"] = "Legacy Tenant",
            ["domain"] = $"{tenantId}.test.com",
            ["created_at"] = DateTime.UtcNow,
            ["created_by"] = "db-test",
            ["enabled"] = true
        });

        var fetched = await _repository.GetByTenantIdAsync(tenantId);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.Metadata);
    }

    [Fact]
    public async Task Document_WithUnknownExtraFields_StillDeserializes()
    {
        // Simulates rollback safety in reverse: a newer schema's extra fields must not
        // break this version's deserialization ([BsonIgnoreExtraElements]).
        var tenantId = $"db-extra-{Guid.NewGuid()}";
        await _rawCollection.InsertOneAsync(new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["tenant_id"] = tenantId,
            ["name"] = "Future Tenant",
            ["domain"] = $"{tenantId}.test.com",
            ["created_at"] = DateTime.UtcNow,
            ["created_by"] = "db-test",
            ["enabled"] = true,
            ["metadata"] = new BsonArray
            {
                new BsonDocument { ["key"] = "K", ["value"] = "V", ["type"] = "PlainText" }
            },
            ["some_future_field"] = "ignored"
        });

        var fetched = await _repository.GetByTenantIdAsync(tenantId);

        Assert.NotNull(fetched);
        Assert.Single(fetched!.Metadata!);
        Assert.Equal("K", fetched.Metadata![0].Key);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesMetadata()
    {
        var tenantId = $"db-update-{Guid.NewGuid()}";
        var tenant = NewTenant(tenantId, new List<TenantMetadata>
        {
            new() { Key = "Region", Value = "WestEurope", Type = MetadataType.PlainText }
        });
        await _repository.CreateAsync(tenant);

        tenant.Metadata = new List<TenantMetadata>
        {
            new() { Key = "Region", Value = "NorthEurope", Type = MetadataType.PlainText },
            new() { Key = "OpenAiKey", Value = "cipher", Type = MetadataType.Secret }
        };
        var updated = await _repository.UpdateAsync(tenant.Id, tenant);

        Assert.True(updated);
        var fetched = await _repository.GetByTenantIdAsync(tenantId);
        Assert.Equal(2, fetched!.Metadata!.Count);
        Assert.Equal("NorthEurope", fetched.Metadata!.Single(m => m.Key == "Region").Value);
    }

    [Fact]
    public async Task UpdateAsync_CanClearMetadata()
    {
        var tenantId = $"db-clear-{Guid.NewGuid()}";
        var tenant = NewTenant(tenantId, new List<TenantMetadata>
        {
            new() { Key = "Region", Value = "WestEurope", Type = MetadataType.PlainText }
        });
        await _repository.CreateAsync(tenant);

        tenant.Metadata = null;
        var updated = await _repository.UpdateAsync(tenant.Id, tenant);

        Assert.True(updated);
        var fetched = await _repository.GetByTenantIdAsync(tenantId);
        Assert.NotNull(fetched);
        Assert.Null(fetched!.Metadata);
    }
}
