using Microsoft.Extensions.Logging.Abstractions;
using Shared.Configuration;
using Shared.Data;
using Shared.Data.Models.Usage;
using Shared.Repositories;
using Shared.Services;
using Tests.TestUtils;

namespace Tests.UnitTests.Shared.Services;

public class TokenUsageServiceTests : IClassFixture<MongoDbFixture>
{
    private readonly MongoDbFixture _fixture;
    private readonly DatabaseService _databaseService;
    private readonly TokenUsageLimitRepository _limitRepository;
    private readonly TokenUsageWindowRepository _windowRepository;
    private readonly TokenUsageEventRepository _eventRepository;
    private readonly TokenUsageService _service;
    private readonly TokenUsageOptions _options = new();

    public TokenUsageServiceTests(MongoDbFixture fixture)
    {
        _fixture = fixture;
        _databaseService = new DatabaseService(fixture.MongoClientService);
        _limitRepository = new TokenUsageLimitRepository(_databaseService, NullLogger<TokenUsageLimitRepository>.Instance);
        _windowRepository = new TokenUsageWindowRepository(_databaseService, NullLogger<TokenUsageWindowRepository>.Instance);
        _eventRepository = new TokenUsageEventRepository(_databaseService, NullLogger<TokenUsageEventRepository>.Instance);
        _service = new TokenUsageService(_limitRepository, _windowRepository, _eventRepository, _options, NullLogger<TokenUsageService>.Instance);
        ClearCollections();
    }

    [Fact]
    public async Task CheckAsync_ShouldReturnDisabled_WhenFeatureOff()
    {
        var disabledOptions = new TokenUsageOptions { Enabled = false };
        var disabledService = new TokenUsageService(_limitRepository, _windowRepository, _eventRepository, disabledOptions, NullLogger<TokenUsageService>.Instance);

        var status = await disabledService.CheckAsync("tenant", "user");

        Assert.False(status.Enabled);
        Assert.False(status.IsExceeded);
    }

    [Fact]
    public async Task CheckAsync_ShouldUseDefaultLimit_WhenNoCustom()
    {
        var status = await _service.CheckAsync("tenant-default", "user-default");

        Assert.True(status.Enabled);
        Assert.Equal(_options.DefaultTenantLimit, status.MaxTokens);
        Assert.Equal(_options.DefaultTenantLimit, status.TokensRemaining);
        Assert.False(status.IsExceeded);
    }

    [Fact]
    public async Task RecordAsync_ShouldIncrementWindowAndLogEvents()
    {
        var tenantId = "tenant-record";
        var userId = "user-record";

        await _service.RecordAsync(new TokenUsageRecord(
            TenantId: tenantId,
            UserId: userId,
            Model: "gpt-4o",
            PromptTokens: 300,
            CompletionTokens: 200,
            WorkflowId: "wf",
            RequestId: "req",
            Source: "unit-test",
            Metadata: new Dictionary<string, string> { ["foo"] = "bar" }));

        var status = await _service.CheckAsync(tenantId, userId);

        Assert.Equal(500, status.TokensUsed);
        Assert.Equal(_options.DefaultTenantLimit - 500, status.TokensRemaining);

        var events = await _eventRepository.GetEventsAsync(tenantId, userId);
        Assert.Single(events);
        Assert.Equal(500, events[0].TotalTokens);
        Assert.Equal("unit-test", events[0].Source);
    }

    [Fact]
    public async Task CheckAsync_ShouldRespectUserOverride()
    {
        await _limitRepository.UpsertAsync(new TokenUsageLimit
        {
            TenantId = "tenant-override",
            UserId = "user-override",
            MaxTokens = 1000,
            WindowSeconds = 3600,
            Enabled = true
        });

        await _service.RecordAsync(new TokenUsageRecord(
            TenantId: "tenant-override",
            UserId: "user-override",
            Model: "model",
            PromptTokens: 600,
            CompletionTokens: 100,
            WorkflowId: null,
            RequestId: null,
            Source: "test",
            Metadata: null));

        var status = await _service.CheckAsync("tenant-override", "user-override");

        Assert.Equal(1000, status.MaxTokens);
        Assert.Equal(700, status.TokensUsed);
        Assert.False(status.IsExceeded);
    }

    [Fact]
    public async Task CheckAsync_ShouldAlignWindowWithEffectiveFromAfterLimitChange()
    {
        var tenantId = "tenant-window-alignment";
        var userId = "user-window-alignment";
        var initialEffectiveFrom = DateTime.UtcNow.AddDays(-1);

        await _limitRepository.UpsertAsync(new TokenUsageLimit
        {
            TenantId = tenantId,
            UserId = null,
            MaxTokens = 2000,
            WindowSeconds = 86400,
            Enabled = true,
            EffectiveFrom = initialEffectiveFrom
        });

        var initialStatus = await _service.CheckAsync(tenantId, userId);
        Assert.Equal(86400, initialStatus.WindowSeconds);

        var updatedEffectiveFrom = DateTime.UtcNow;
        await _limitRepository.UpsertAsync(new TokenUsageLimit
        {
            TenantId = tenantId,
            UserId = null,
            MaxTokens = 2000,
            WindowSeconds = 10000,
            Enabled = true,
            EffectiveFrom = updatedEffectiveFrom
        });

        var updatedStatus = await _service.CheckAsync(tenantId, userId);

        Assert.Equal(10000, updatedStatus.WindowSeconds);
        Assert.InRange(updatedStatus.WindowStart, updatedEffectiveFrom.AddSeconds(-2), updatedEffectiveFrom.AddSeconds(2));
        var actualWindowLength = (updatedStatus.WindowEndsAt - updatedStatus.WindowStart).TotalSeconds;
        Assert.InRange(actualWindowLength, updatedStatus.WindowSeconds - 1, updatedStatus.WindowSeconds + 1);
    }

    [Fact]
    public async Task CheckAsync_ShouldUseTenantLimit_WhenUserIdStoredAsEmptyString()
    {
        var tenantId = "tenant-empty-user";
        await _limitRepository.UpsertAsync(new TokenUsageLimit
        {
            TenantId = tenantId,
            UserId = string.Empty,
            MaxTokens = 500,
            WindowSeconds = 7200,
            Enabled = true
        });

        var status = await _service.CheckAsync(tenantId, "some-user");

        Assert.Equal(500, status.MaxTokens);
        Assert.Equal(7200, status.WindowSeconds);
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
                _fixture.Database.DropCollection(name);
            }
            catch
            {
                // ignore
            }
        }
    }
}

