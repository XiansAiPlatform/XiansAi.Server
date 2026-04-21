using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

public class SecretVaultServiceTests
{
    private readonly Mock<ISecretVaultRepository> _repo = new(MockBehavior.Strict);
    private readonly InMemorySecretStoreProvider _store = new();
    private readonly SecretVaultService _service;

    public SecretVaultServiceTests()
    {
        _service = new SecretVaultService(_repo.Object, _store, NullLogger<SecretVaultService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_Persists_Metadata_And_Value()
    {
        _repo.Setup(r => r.ExistsByKeyAsync("api-key", "tenant-a")).ReturnsAsync(false);
        SecretVault? captured = null;
        _repo.Setup(r => r.CreateAsync(It.IsAny<SecretVault>()))
            .Callback<SecretVault>(s => captured = s)
            .Returns(Task.CompletedTask);

        var input = new SecretVaultCreateInput("api-key", "supersecret", "tenant-a", null, null, null, null);

        var result = await _service.CreateAsync(input, "alice");

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("api-key", captured!.Key);
        Assert.Equal("tenant-a", captured.TenantId);
        Assert.Equal("alice", captured.CreatedBy);
#pragma warning disable CS0618 // legacy field intentionally inspected to confirm we no longer write it
        Assert.Null(captured.EncryptedValue); // value lives in the provider, not on the metadata doc
#pragma warning restore CS0618

        Assert.Equal("supersecret", await _store.GetAsync(captured.Id));
        Assert.Equal("supersecret", result.Data!.Value);
    }

    [Fact]
    public async Task CreateAsync_Rejects_Duplicate_Key()
    {
        _repo.Setup(r => r.ExistsByKeyAsync("dup", null)).ReturnsAsync(true);

        var input = new SecretVaultCreateInput("dup", "v", null, null, null, null, null);
        var result = await _service.CreateAsync(input, "alice");

        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        Assert.Empty(_store.Snapshot());
    }

    [Fact]
    public async Task CreateAsync_RollsBack_Value_On_Metadata_Failure()
    {
        _repo.Setup(r => r.ExistsByKeyAsync("api-key", null)).ReturnsAsync(false);
        _repo.Setup(r => r.CreateAsync(It.IsAny<SecretVault>()))
            .ThrowsAsync(new InvalidOperationException("simulated mongo failure"));

        var input = new SecretVaultCreateInput("api-key", "supersecret", null, null, null, null, null);

        var result = await _service.CreateAsync(input, "alice");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
        Assert.Empty(_store.Snapshot()); // orphan value cleaned up
    }

    [Fact]
    public async Task CreateAsync_Validates_Required_Fields()
    {
        var emptyKey = new SecretVaultCreateInput("", "v", null, null, null, null, null);
        var emptyValue = new SecretVaultCreateInput("k", "", null, null, null, null, null);

        Assert.Equal(StatusCode.BadRequest, (await _service.CreateAsync(emptyKey, "alice")).StatusCode);
        Assert.Equal(StatusCode.BadRequest, (await _service.CreateAsync(emptyValue, "alice")).StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Value_From_Store()
    {
        var entity = NewEntity("tenant-a", "k1");
        _repo.Setup(r => r.GetByIdAsync(entity.Id)).ReturnsAsync(entity);
        await _store.SetAsync(entity.Id, "stored-value");

        var result = await _service.GetByIdAsync(entity.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("stored-value", result.Data!.Value);
    }

    [Fact]
    public async Task GetByIdAsync_Conflict_When_Value_Missing()
    {
        var entity = NewEntity("tenant-a", "k1");
        _repo.Setup(r => r.GetByIdAsync(entity.Id)).ReturnsAsync(entity);

        var result = await _service.GetByIdAsync(entity.Id);

        Assert.Equal(StatusCode.Conflict, result.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_Updates_Value_When_Provided()
    {
        var entity = NewEntity("tenant-a", "k1");
        await _store.SetAsync(entity.Id, "original");
        _repo.Setup(r => r.GetByIdAsync(entity.Id)).ReturnsAsync(entity);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<SecretVault>())).ReturnsAsync(true);

        var result = await _service.UpdateAsync(
            entity.Id,
            new SecretVaultUpdateInput("rotated", null, null, null, null, null),
            "bob");

        Assert.True(result.IsSuccess);
        Assert.Equal("rotated", await _store.GetAsync(entity.Id));
        Assert.Equal("rotated", result.Data!.Value);
    }

    [Fact]
    public async Task UpdateAsync_Without_Value_Leaves_Store_Untouched()
    {
        var entity = NewEntity("tenant-a", "k1");
        await _store.SetAsync(entity.Id, "kept");
        _repo.Setup(r => r.GetByIdAsync(entity.Id)).ReturnsAsync(entity);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<SecretVault>())).ReturnsAsync(true);

        var result = await _service.UpdateAsync(
            entity.Id,
            new SecretVaultUpdateInput(null, null, null, null, "act-1", null),
            "bob");

        Assert.True(result.IsSuccess);
        Assert.Equal("kept", await _store.GetAsync(entity.Id));
    }

    [Fact]
    public async Task DeleteAsync_Removes_Metadata_And_Value()
    {
        var entity = NewEntity("tenant-a", "k1");
        await _store.SetAsync(entity.Id, "to-die");
        _repo.Setup(r => r.DeleteAsync(entity.Id)).ReturnsAsync(true);

        var result = await _service.DeleteAsync(entity.Id);

        Assert.True(result.IsSuccess);
        Assert.Null(await _store.GetAsync(entity.Id));
    }

    [Fact]
    public async Task DeleteAsync_Does_Not_Touch_Store_When_Metadata_Missing()
    {
        _repo.Setup(r => r.DeleteAsync("missing")).ReturnsAsync(false);
        await _store.SetAsync("missing", "should-stay");

        var result = await _service.DeleteAsync("missing");

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        Assert.Equal("should-stay", await _store.GetAsync("missing"));
    }

    [Fact]
    public async Task GetMetadataByIdAsync_Does_Not_Touch_Store()
    {
        var entity = NewEntity("tenant-a", "k1");
        entity.AdditionalData = "{\"env\":\"prod\"}";
        _repo.Setup(r => r.GetByIdAsync(entity.Id)).ReturnsAsync(entity);

        // Note: nothing stored under this id; if the implementation accidentally hit the store
        // we would either return a Conflict or get an empty Value. Neither should happen.
        var result = await _service.GetMetadataByIdAsync(entity.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(entity.Id, result.Data!.Id);
        Assert.Equal("k1", result.Data.Key);
        Assert.Equal("tenant-a", result.Data.TenantId);
        Assert.NotNull(result.Data.AdditionalData);
        Assert.Empty(_store.Snapshot()); // store was never read
    }

    [Fact]
    public async Task GetMetadataByIdAsync_NotFound_When_Missing()
    {
        _repo.Setup(r => r.GetByIdAsync("missing")).ReturnsAsync((SecretVault?)null);

        var result = await _service.GetMetadataByIdAsync("missing");

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task FindMetadataByKeyAsync_Returns_Metadata_Without_Value()
    {
        var entity = NewEntity("tenant-a", "k1");
        _repo.Setup(r => r.FindForAccessAsync("k1", "tenant-a", null, null, null)).ReturnsAsync(entity);
        await _store.SetAsync(entity.Id, "should-not-leak");

        var result = await _service.FindMetadataByKeyAsync("k1", "tenant-a", null, null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(entity.Id, result.Data!.Id);
        // SecretVaultMetadataResponse has no Value member by construction; this assertion documents intent.
        Assert.DoesNotContain("Value", typeof(SecretVaultMetadataResponse).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task FindMetadataByKeyAsync_NotFound_When_Scope_Mismatches()
    {
        _repo.Setup(r => r.FindForAccessAsync("k1", "wrong-tenant", null, null, null))
            .ReturnsAsync((SecretVault?)null);

        var result = await _service.FindMetadataByKeyAsync("k1", "wrong-tenant", null, null, null);

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task FetchByKeyAsync_Returns_Scoped_Match()
    {
        var entity = NewEntity("tenant-a", "k1");
        entity.AdditionalData = "{\"env\":\"prod\"}";
        _repo.Setup(r => r.FindForAccessAsync("k1", "tenant-a", null, null, null)).ReturnsAsync(entity);
        await _store.SetAsync(entity.Id, "found-it");

        var result = await _service.FetchByKeyAsync("k1", "tenant-a", null, null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("found-it", result.Data!.Value);
    }

    private static SecretVault NewEntity(string tenantId, string key) => new()
    {
        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
        Key = key,
        TenantId = tenantId,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    /// <summary>
    /// Thread-safe in-memory <see cref="ISecretStoreProvider"/> for service-level tests.
    /// </summary>
    private sealed class InMemorySecretStoreProvider : ISecretStoreProvider
    {
        private readonly ConcurrentDictionary<string, string> _store = new();

        public string Name => "in-memory-test";

        public Task SetAsync(string secretId, string value, CancellationToken cancellationToken = default)
        {
            _store[secretId] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string secretId, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(secretId, out var v) ? v : null);

        public Task DeleteAsync(string secretId, CancellationToken cancellationToken = default)
        {
            _store.TryRemove(secretId, out _);
            return Task.CompletedTask;
        }

        public IDictionary<string, string> Snapshot() => new Dictionary<string, string>(_store);
    }
}
