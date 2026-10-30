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
/// DB-level tests for <see cref="WebhookIncomingRepository"/> against the Mongo2Go fixture.
/// Each test uses its own tenant id so tests stay independent within the shared collection.
/// </summary>
public class WebhookIncomingRepositoryTests : IClassFixture<MongoDbFixture>
{
    private const string Integration = "integration-1";
    private const string WorkflowId = "tenant:AI-DLC Agent v1:PR Review:Raintree";

    private static readonly DateTime Start = new(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc);

    private readonly IMongoDatabase _database;
    private readonly WebhookIncomingRepository _repository;

    public WebhookIncomingRepositoryTests(MongoDbFixture fixture)
    {
        _database = fixture.Database;
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(_database);
        _repository = new WebhookIncomingRepository(databaseService.Object, NullLogger<WebhookIncomingRepository>.Instance);
    }

    private static string NewTenant() => $"tenant-{Guid.NewGuid():N}";

    private static WebhookIncoming Incoming(string tenantId, int minute, string integrationId = Integration) => new()
    {
        TenantId = tenantId,
        IntegrationId = integrationId,
        RequestId = $"request-{minute}",
        ReceivedAt = Start.AddMinutes(minute),
        ContentType = "application/json",
        Headers =
        [
            new() { Name = "X-GitHub-Event", Value = "pull_request" },
            new() { Name = "X-Hub-Signature-256", Value = "sha256=abc" }
        ],
        Body = "{\"action\":\"opened\"}",
        WorkflowId = WorkflowId
    };

    private async Task<List<WebhookIncoming>> InsertMinutesAsync(string tenantId, params int[] minutes)
    {
        var rows = minutes.Select(m => Incoming(tenantId, m)).ToList();
        foreach (var row in rows)
        {
            await _repository.InsertAsync(row);
        }
        return rows;
    }

    [Fact]
    public async Task InsertStoresSnakeCaseFieldsAndUuidId()
    {
        var tenant = NewTenant();
        var row = (await InsertMinutesAsync(tenant, 0))[0];

        var raw = await _database.GetCollection<BsonDocument>("webhook_incomings")
            .Find(new BsonDocument("tenant_id", tenant)).SingleAsync();

        Assert.Equal(BsonType.Binary, raw["_id"].BsonType);
        Assert.Equal(BsonBinarySubType.UuidStandard, raw["_id"].AsBsonBinaryData.SubType);
        Assert.Equal(row.Id, raw["_id"].AsGuid);
        foreach (var field in new[] { "integration_id", "request_id", "received_at", "content_type", "headers", "body", "workflow_id" })
        {
            Assert.True(raw.Contains(field), $"missing {field}");
        }
    }

    [Fact]
    public async Task InsertingTheSameCallTwiceIsNotAnError()
    {
        var tenant = NewTenant();
        var row = Incoming(tenant, 0);

        await _repository.InsertAsync(row);
        await _repository.InsertAsync(row);

        Assert.Single(await _repository.ListAsync(tenant, Integration, null, 10));
    }

    [Fact]
    public void ReceivedAtDefaultsToNow()
    {
        var row = new WebhookIncoming { TenantId = "t", IntegrationId = "i", RequestId = "r", WorkflowId = "w" };

        Assert.Equal(DateTimeKind.Utc, row.ReceivedAt.Kind);
        Assert.True(DateTime.UtcNow - row.ReceivedAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CursorWithUnspecifiedKindIsTreatedAsUtc()
    {
        var tenant = NewTenant();
        await InsertMinutesAsync(tenant, 0, 1, 2);
        var first = (await _repository.ListAsync(tenant, Integration, null, 1)).Single();

        var unspecified = new WebhookIncomingCursor(DateTime.SpecifyKind(first.ReceivedAt, DateTimeKind.Unspecified), first.Id);
        var next = await _repository.ListAsync(tenant, Integration, unspecified, 10);

        Assert.Equal(new[] { "request-1", "request-0" }, next.Select(r => r.RequestId));
    }

    [Fact]
    public async Task GetReturnsFullRowOnlyForMatchingTenantAndIntegration()
    {
        var tenant = NewTenant();
        var row = (await InsertMinutesAsync(tenant, 0))[0];

        var found = await _repository.GetAsync(tenant, Integration, row.Id);
        Assert.NotNull(found);
        Assert.Equal(row.Body, found!.Body);
        Assert.Equal("sha256=abc", found.Headers.Single(h => h.Name == "X-Hub-Signature-256").Value);

        Assert.Null(await _repository.GetAsync(NewTenant(), Integration, row.Id));
        Assert.Null(await _repository.GetAsync(tenant, "other-integration", row.Id));
    }

    [Fact]
    public async Task ListReturnsNewestFirst()
    {
        var tenant = NewTenant();
        var rows = await InsertMinutesAsync(tenant, 1, 3, 2);

        var page = await _repository.ListAsync(tenant, Integration, after: null, limit: 10);

        Assert.Equal(new[] { "request-3", "request-2", "request-1" }, page.Select(r => r.RequestId));
        Assert.Equal(rows[1].Id, page[0].Id);
        Assert.Equal(rows[1].ReceivedAt, page[0].ReceivedAt);
    }

    [Fact]
    public async Task RepeatedHeadersAreKeptInOrder()
    {
        var tenant = NewTenant();
        var row = Incoming(tenant, 0);
        row.Headers.Add(new() { Name = "X-Forwarded-For", Value = "10.0.0.1" });
        row.Headers.Add(new() { Name = "X-Forwarded-For", Value = "10.0.0.2" });
        row.Headers.Add(new() { Name = "X-Odd.$Name", Value = "kept" });
        await _repository.InsertAsync(row);

        var found = await _repository.GetAsync(tenant, Integration, row.Id);

        Assert.Equal(
            row.Headers.Select(h => (h.Name, h.Value)),
            found!.Headers.Select(h => (h.Name, h.Value)));
    }

    [Fact]
    public async Task ListLimitIsCappedAtMaxPageSize()
    {
        var tenant = NewTenant();
        var rows = Enumerable.Range(0, WebhookIncomingRepository.MaxPageSize + 5)
            .Select(m => Incoming(tenant, m))
            .ToList();
        await _database.GetCollection<WebhookIncoming>("webhook_incomings").InsertManyAsync(rows);

        Assert.Equal(WebhookIncomingRepository.MaxPageSize,
            (await _repository.ListAsync(tenant, Integration, null, int.MaxValue)).Count);
        Assert.Equal(WebhookIncomingRepository.MaxPageSize,
            (await _repository.ListByWorkflowAsync(tenant, WorkflowId, int.MaxValue)).Count);
    }

    [Fact]
    public async Task ListPagesWithCursorIncludingSameTimestamp()
    {
        var tenant = NewTenant();
        // Two rows share a timestamp, so the cursor has to fall back to _id.
        var rows = new[] { Incoming(tenant, 0), Incoming(tenant, 1), Incoming(tenant, 1), Incoming(tenant, 2) };
        foreach (var row in rows)
        {
            await _repository.InsertAsync(row);
        }

        var seen = new List<Guid>();
        WebhookIncomingCursor? cursor = null;
        while (true)
        {
            var page = await _repository.ListAsync(tenant, Integration, cursor, limit: 1);
            if (page.Count == 0)
            {
                break;
            }
            seen.Add(page[0].Id);
            cursor = new WebhookIncomingCursor(page[0].ReceivedAt, page[0].Id);
        }

        Assert.Equal(4, seen.Count);
        Assert.Equal(rows.Select(r => r.Id).ToHashSet(), seen.ToHashSet());
    }

    [Fact]
    public async Task EveryReadIsScopedToTenant()
    {
        var tenant = NewTenant();
        var otherTenant = NewTenant();
        await InsertMinutesAsync(tenant, 0);
        await InsertMinutesAsync(otherTenant, 1);

        Assert.Single(await _repository.ListAsync(tenant, Integration, null, 10));
        Assert.Single(await _repository.ListByWorkflowAsync(tenant, WorkflowId, 10));
        Assert.Equal(1, await _repository.DeleteByIntegrationAsync(tenant, Integration));
        Assert.Single(await _repository.ListAsync(otherTenant, Integration, null, 10));
    }

    [Fact]
    public async Task ListByWorkflowReturnsNewestFirst()
    {
        var tenant = NewTenant();
        await InsertMinutesAsync(tenant, 5, 7, 6);

        var rows = await _repository.ListByWorkflowAsync(tenant, WorkflowId, limit: 2);

        Assert.Equal(new[] { "request-7", "request-6" }, rows.Select(r => r.RequestId));
    }

    [Fact]
    public async Task TrimKeepsOnlyTheNewestRows()
    {
        var tenant = NewTenant();
        await InsertMinutesAsync(tenant, 0, 1, 2, 3, 4);
        await _repository.InsertAsync(Incoming(tenant, 0, "other-integration"));

        var deleted = await _repository.TrimAsync(tenant, Integration, keep: 3);

        Assert.Equal(2, deleted);
        var left = await _repository.ListAsync(tenant, Integration, null, 10);
        Assert.Equal(new[] { "request-4", "request-3", "request-2" }, left.Select(r => r.RequestId));
        Assert.Single(await _repository.ListAsync(tenant, "other-integration", null, 10));
    }

    [Fact]
    public async Task TrimDoesNothingUnderTheCap()
    {
        var tenant = NewTenant();
        await InsertMinutesAsync(tenant, 0, 1);

        Assert.Equal(0, await _repository.TrimAsync(tenant, Integration, keep: 3));
        Assert.Equal(2, (await _repository.ListAsync(tenant, Integration, null, 10)).Count);
    }

    [Fact]
    public async Task DeleteByIntegrationRemovesOnlyThatConnection()
    {
        var tenant = NewTenant();
        await InsertMinutesAsync(tenant, 0, 1);
        await _repository.InsertAsync(Incoming(tenant, 2, "other-integration"));

        Assert.Equal(2, await _repository.DeleteByIntegrationAsync(tenant, Integration));
        Assert.Empty(await _repository.ListAsync(tenant, Integration, null, 10));
        Assert.Single(await _repository.ListAsync(tenant, "other-integration", null, 10));
    }
}
