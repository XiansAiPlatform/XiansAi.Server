using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models.Usage;
using Shared.Repositories;
using Tests.TestUtils;

namespace Tests.UnitTests.Shared.Repositories;

public class TokenUsageRepositoryTests : IClassFixture<MongoDbFixture>
{
    private readonly MongoDbFixture _fixture;
    private readonly DatabaseService _databaseService;
    private readonly TokenUsageWindowRepository _windowRepository;
    private readonly TokenUsageLimitRepository _limitRepository;
    private readonly IMongoDatabase _database;

    public TokenUsageRepositoryTests(MongoDbFixture fixture)
    {
        _fixture = fixture;
        _database = fixture.Database;
        _databaseService = new DatabaseService(fixture.MongoClientService);
        _windowRepository = new TokenUsageWindowRepository(_databaseService, NullLogger<TokenUsageWindowRepository>.Instance);
        _limitRepository = new TokenUsageLimitRepository(_databaseService, NullLogger<TokenUsageLimitRepository>.Instance);
        ClearCollections();
    }

    [Fact]
    public async Task IncrementWindowAsync_ShouldCreateAndAccumulateTokens()
    {
        // Arrange
        var tenantId = "tenant-token-test";
        var userId = "user-123";
        var windowSeconds = 3600;
        var windowStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var first = await _windowRepository.IncrementWindowAsync(tenantId, userId, windowStart, windowSeconds, 500);
        var second = await _windowRepository.IncrementWindowAsync(tenantId, userId, windowStart, windowSeconds, 250);

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(tenantId, second.TenantId);
        Assert.Equal(userId, second.UserId);
        Assert.Equal(windowStart, second.WindowStart);
        Assert.Equal(windowSeconds, second.WindowSeconds);
        Assert.Equal(750, second.TokensUsed);
    }

    [Fact]
    public async Task GetWindowAsync_ShouldReturnNullForUnknownWindow()
    {
        var window = await _windowRepository.GetWindowAsync("missing-tenant", "missing-user", DateTime.UtcNow, 3600);
        Assert.Null(window);
    }

    [Fact]
    public async Task UpsertLimitAsync_ShouldCreateAndUpdateLimit()
    {
        // Arrange
        var limit = new TokenUsageLimit
        {
            TenantId = "tenant-limit-test",
            UserId = "special-user",
            MaxTokens = 10_000,
            WindowSeconds = 7200,
            Enabled = true,
            UpdatedBy = "unit-test"
        };

        // Act - insert
        var created = await _limitRepository.UpsertAsync(limit);

        // Act - update
        created.MaxTokens = 20_000;
        created.WindowSeconds = 14400;
        var updated = await _limitRepository.UpsertAsync(created);

        // Assert
        Assert.NotNull(created.Id);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(20_000, updated.MaxTokens);
        Assert.Equal(14400, updated.WindowSeconds);

        var loaded = await _limitRepository.GetUserLimitAsync(limit.TenantId, limit.UserId!);
        Assert.NotNull(loaded);
        Assert.Equal(updated.MaxTokens, loaded!.MaxTokens);
    }

    private void ClearCollections()
    {
        var names = new[]
        {
            "token_usage_windows",
            "token_usage_limits",
            "token_usage_events"
        };

        foreach (var name in names)
        {
            try
            {
                _database.DropCollection(name);
            }
            catch
            {
                // ignore if it doesn't exist yet
            }
        }
    }
}

