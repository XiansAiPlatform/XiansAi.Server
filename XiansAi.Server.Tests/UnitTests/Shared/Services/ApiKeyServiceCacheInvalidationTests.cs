using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services;

public class ApiKeyServiceCacheInvalidationTests
{
    private const string TenantId = "tenant-a";
    private const string HashedKey = "stored-hash";

    private readonly Mock<IApiKeyRepository> _repository = new();
    private readonly Mock<ICacheInvalidationBus> _bus = new();

    private ApiKeyService BuildService() => new(
        _repository.Object,
        NullLogger<ApiKeyService>.Instance,
        new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
        Mock.Of<IWebhookEventPublisher>(),
        _bus.Object);

    private static ApiKey ExistingKey() => new()
    {
        Id = "key-id",
        TenantId = TenantId,
        Name = "test",
        HashedKey = HashedKey,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "creator"
    };

    [Fact]
    public async Task RevokeApiKey_PublishesTenantAndAuthenticationCacheKeys()
    {
        _repository.Setup(x => x.GetByIdAsync("key-id", TenantId)).ReturnsAsync(ExistingKey());
        _repository.Setup(x => x.RevokeAsync("key-id", TenantId)).ReturnsAsync(true);

        var result = await BuildService().RevokeApiKeyAsync("key-id", TenantId);

        Assert.True(result.IsSuccess);
        _bus.Verify(x => x.PublishAsync(
            It.Is<CacheInvalidationEnvelope>(envelope =>
                envelope.Type == CacheInvalidationType.ApiKey &&
                envelope.Keys != null &&
                envelope.Keys.Count == 2 &&
                envelope.Keys.Contains($"apikey:{TenantId}:{HashedKey}") &&
                envelope.Keys.Contains($"apikey:auth:{HashedKey}")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RotateApiKey_PublishesTenantAndAuthenticationCacheKeysForOldKey()
    {
        var existing = ExistingKey();
        _repository.Setup(x => x.GetByIdAsync("key-id", TenantId)).ReturnsAsync(existing);
        _repository.Setup(x => x.RotateAsync("key-id", TenantId))
            .ReturnsAsync(("raw-new-key", ExistingKey()));

        var result = await BuildService().RotateApiKeyAsync("key-id", TenantId);

        Assert.True(result.IsSuccess);
        _bus.Verify(x => x.PublishAsync(
            It.Is<CacheInvalidationEnvelope>(envelope =>
                envelope.Type == CacheInvalidationType.ApiKey &&
                envelope.Keys != null &&
                envelope.Keys.Count == 2 &&
                envelope.Keys.Contains($"apikey:{TenantId}:{HashedKey}") &&
                envelope.Keys.Contains($"apikey:auth:{HashedKey}")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAllApiKeys_PublishesBothCacheKeysForEveryStoredKey()
    {
        var second = ExistingKey();
        second.TenantId = "tenant-b";
        second.HashedKey = "second-hash";
        _repository.Setup(x => x.GetAllAsync()).ReturnsAsync([ExistingKey(), second]);
        _repository.Setup(x => x.DeleteAllAsync()).ReturnsAsync(2);

        var result = await BuildService().DeleteAllApiKeysAsync();

        Assert.True(result.IsSuccess);
        _bus.Verify(x => x.PublishAsync(
            It.Is<CacheInvalidationEnvelope>(envelope =>
                envelope.TenantId == TenantId &&
                envelope.Keys != null &&
                envelope.Keys.Contains($"apikey:{TenantId}:{HashedKey}") &&
                envelope.Keys.Contains($"apikey:auth:{HashedKey}")),
            It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(x => x.PublishAsync(
            It.Is<CacheInvalidationEnvelope>(envelope =>
                envelope.TenantId == "tenant-b" &&
                envelope.Keys != null &&
                envelope.Keys.Contains("apikey:tenant-b:second-hash") &&
                envelope.Keys.Contains("apikey:auth:second-hash")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
