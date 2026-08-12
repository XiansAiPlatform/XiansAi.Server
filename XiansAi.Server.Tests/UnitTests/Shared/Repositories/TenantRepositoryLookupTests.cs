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
/// DB-level tests for the case-insensitive tenant lookup used to enforce unique tenant ids.
/// Runs against a real MongoDB (ephemeral fixture) so the query itself is exercised, not mocked.
/// </summary>
public class TenantRepositoryLookupTests : IClassFixture<MongoDbFixture>
{
    private readonly TenantRepository _repository;

    public TenantRepositoryLookupTests(MongoDbFixture fixture)
    {
        var collection = fixture.Database.GetCollection<Tenant>("tenants");

        var clientService = new Mock<IMongoDbClientService>();
        clientService.Setup(x => x.GetCollection<Tenant>("tenants")).Returns(collection);
        _repository = new TenantRepository(clientService.Object, NullLogger<TenantRepository>.Instance);
    }

    private static Tenant NewTenant(string tenantId)
    {
        return new Tenant
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            Name = $"Tenant {tenantId}",
            Domain = $"{tenantId}.test.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "db-test"
        };
    }

    [Fact]
    public async Task GetByTenantIdCaseInsensitive_FindsTenant_WhenCasingMatchesExactly()
    {
        var tenantId = $"db-lookup-{Guid.NewGuid()}";
        await _repository.CreateAsync(NewTenant(tenantId));

        var found = await _repository.GetByTenantIdCaseInsensitiveAsync(tenantId);

        Assert.Equal(tenantId, found?.TenantId);
    }

    [Fact]
    public async Task GetByTenantIdCaseInsensitive_FindsTenant_AndReturnsTheStoredCasing()
    {
        var tenantId = $"DB-Lookup-Mixed-{Guid.NewGuid()}";
        await _repository.CreateAsync(NewTenant(tenantId));

        var found = await _repository.GetByTenantIdCaseInsensitiveAsync(tenantId.ToLowerInvariant());

        Assert.Equal(tenantId, found?.TenantId);
    }

    [Fact]
    public async Task GetByTenantIdCaseInsensitive_ReturnsNull_WhenNoTenantMatches()
    {
        var found = await _repository.GetByTenantIdCaseInsensitiveAsync($"db-missing-{Guid.NewGuid()}");

        Assert.Null(found);
    }

    [Fact]
    public async Task GetByTenantIdCaseInsensitive_DoesNotMatchOnPartialTenantId()
    {
        var tenantId = $"db-prefix-{Guid.NewGuid()}";
        await _repository.CreateAsync(NewTenant(tenantId + "-suffix"));

        var found = await _repository.GetByTenantIdCaseInsensitiveAsync(tenantId);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetByTenantIdCaseInsensitive_TreatsRegexCharactersLiterally()
    {
        var unique = Guid.NewGuid().ToString("N");
        // A '.' must not behave as a regex wildcard: "db.dot-x" must not match "dbXdot-x".
        await _repository.CreateAsync(NewTenant($"db{unique}dot"));

        var found = await _repository.GetByTenantIdCaseInsensitiveAsync($"db.{unique.Substring(1)}dot");

        Assert.Null(found);
    }

    [Fact]
    public async Task GetByTenantIdCaseInsensitive_Throws_WhenTenantIdIsBlank()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.GetByTenantIdCaseInsensitiveAsync("  "));
    }
}
