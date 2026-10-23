using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Shared.Data;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

/// <summary>
/// Runs against MongoDB so the atomic claim and scrub operations are exercised instead of being mocked.
/// </summary>
public class PrivacyRequestRepositoryTests : IClassFixture<MongoDbFixture>
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private readonly PrivacyRequestRepository _repository;
    private readonly IMongoCollection<PrivacyRequest> _collection;

    public PrivacyRequestRepositoryTests(MongoDbFixture fixture)
    {
        // Each test gets its own collection contents because the claim tests take every due request.
        fixture.Database.DropCollection("privacy_requests");
        _collection = fixture.Database.GetCollection<PrivacyRequest>("privacy_requests");

        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(fixture.Database);
        _repository = new PrivacyRequestRepository(databaseService.Object, NullLogger<PrivacyRequestRepository>.Instance);
    }

    private static PrivacyRequest NewErasure(
        string requester = "keycloak|alice",
        PrivacyActorType actor = PrivacyActorType.Self,
        DateTime? runAfter = null,
        params string[] tenants) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Type = PrivacyRequestType.Erasure,
        Subject = new PrivacySubjectInput { Kind = PrivacySubjectKind.UserId, Value = "keycloak|alice" },
        Scope = new PrivacyScope { TenantIds = tenants.Length == 0 ? ["t1"] : tenants.ToList() },
        RequestedBy = new PrivacyRequester { UserId = requester, UserHash = Hash(requester), ActorType = actor },
        SubjectHash = "hash-of-input",
        CreatedAt = Now.AddDays(-8),
        RunAfter = runAfter ?? Now.AddDays(-1)
    };

    // Opaque like the real HMAC, so the scrub checks can look for the plain name in the stored document.
    private static string Hash(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));

    private async Task<PrivacyRequest> InsertAsync(PrivacyRequest request)
    {
        await _repository.CreateAsync(request);
        return request;
    }

    private async Task<PrivacyRequest> ClaimOneAsync(string instance = "instance-a", DateTime? at = null)
    {
        return Assert.Single(await _repository.ClaimDueBatchAsync(instance, at ?? Now, Lease, 10));
    }

    private static PrivacyContributorResult Result(string name, PrivacyContributorStatus status, long items = 0) => new()
    {
        Name = name, Status = status, ItemsAffected = items, CompletedAt = Now
    };

    [Fact]
    public async Task Create_ThenGet_RoundTrips()
    {
        var request = await InsertAsync(NewErasure());

        var stored = await _repository.GetByIdAsync(request.Id);

        Assert.NotNull(stored);
        Assert.Equal(PrivacyRequestStatus.Pending, stored.Status);
        Assert.Equal("keycloak|alice", stored.Subject.Value);
        Assert.Equal(["t1"], stored.Scope.TenantIds);
    }

    [Fact]
    public async Task Create_RepeatedAfterAnUnclearFailure_KeepsOneRequest()
    {
        var request = await InsertAsync(NewErasure());

        await _repository.CreateAsync(request);

        Assert.Equal(1, await _collection.CountDocumentsAsync(Builders<PrivacyRequest>.Filter.Empty));
    }

    [Fact]
    public async Task Cancel_WhilePending_ByRequester_Succeeds()
    {
        var request = await InsertAsync(NewErasure());

        Assert.True(await _repository.CancelAsync(request.Id, Hash("keycloak|alice"), Now));

        var stored = await _repository.GetByIdAsync(request.Id);
        Assert.Equal(PrivacyRequestStatus.Cancelled, stored!.Status);
        Assert.Equal(Now, stored.CancelledAt);
        Assert.Equal(Now, stored.ClosedAt);
    }

    [Fact]
    public async Task Cancel_ScrubsIdentifiersAndStaysListedForTheRequester()
    {
        var request = await InsertAsync(NewErasure());

        await _repository.CancelAsync(request.Id, Hash("keycloak|alice"), Now);

        var stored = (await _repository.GetByIdAsync(request.Id))!;
        Assert.Null(stored.Subject.Value);
        Assert.Null(stored.RequestedBy.UserId);
        Assert.Equal(Hash("keycloak|alice"), stored.RequestedBy.UserHash);
        Assert.Equal([request.Id], (await _repository.ListByRequesterAsync(Hash("keycloak|alice"), 50)).Select(r => r.Id));
    }

    [Fact]
    public async Task Cancel_KeepsAnAdminRequester()
    {
        var request = await InsertAsync(NewErasure(requester: "keycloak|admin", actor: PrivacyActorType.TenantAdmin));

        await _repository.CancelAsync(request.Id, Hash("keycloak|admin"), Now);

        var stored = (await _repository.GetByIdAsync(request.Id))!;
        Assert.Equal("keycloak|admin", stored.RequestedBy.UserId);
        Assert.Null(stored.Subject.Value);
    }

    [Fact]
    public async Task ExportDelivered_IsStoredCompletedScrubbedAndNeverClaimed()
    {
        var export = NewErasure();
        export.Type = PrivacyRequestType.Export;

        await _repository.RecordExportDeliveredAsync(export, Now);

        var stored = (await _repository.GetByIdAsync(export.Id))!;
        Assert.Equal(PrivacyRequestStatus.Completed, stored.Status);
        Assert.Equal(Now, stored.CompletedAt);
        Assert.Equal(Now, stored.ClosedAt);
        Assert.Null(stored.Subject.Value);
        Assert.Null(stored.RequestedBy.UserId);
        Assert.Equal([export.Id], (await _repository.ListByRequesterAsync(Hash("keycloak|alice"), 50)).Select(r => r.Id));
        Assert.Empty(await _repository.ClaimDueBatchAsync("instance-a", Now, Lease, 10));
    }

    [Fact]
    public async Task ExportDelivered_RefusesAnErasure()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.RecordExportDeliveredAsync(NewErasure(), Now));
    }

    [Fact]
    public async Task Cancel_BySomeoneElse_IsRefused()
    {
        var request = await InsertAsync(NewErasure());

        Assert.False(await _repository.CancelAsync(request.Id, Hash("keycloak|mallory"), Now));
        Assert.Equal(PrivacyRequestStatus.Pending, (await _repository.GetByIdAsync(request.Id))!.Status);
    }

    [Fact]
    public async Task Cancel_AfterItStarted_IsRefused()
    {
        var request = await InsertAsync(NewErasure());
        await ClaimOneAsync();

        Assert.False(await _repository.CancelAsync(request.Id, Hash("keycloak|alice"), Now));
        Assert.Equal(PrivacyRequestStatus.Running, (await _repository.GetByIdAsync(request.Id))!.Status);
    }

    [Fact]
    public async Task Claim_TakesOnlyDueErasures()
    {
        var due = await InsertAsync(NewErasure());
        await InsertAsync(NewErasure(runAfter: Now.AddDays(3)));
        var cancelled = await InsertAsync(NewErasure());
        await _repository.CancelAsync(cancelled.Id, Hash("keycloak|alice"), Now);
        var export = NewErasure();
        export.Type = PrivacyRequestType.Export;
        await InsertAsync(export);

        var only = await ClaimOneAsync();

        Assert.Equal(due.Id, only.Id);
        Assert.Equal(PrivacyRequestStatus.Running, only.Status);
        Assert.Equal("instance-a", only.ClaimedBy);
        Assert.Equal(Now.Add(Lease), only.LeaseExpiresAt);
        Assert.False(string.IsNullOrEmpty(only.LeaseId));
        Assert.Equal(1, only.Attempts);
    }

    [Fact]
    public async Task Claim_TwoInstancesRacing_OnlyOneWinsEachRequest()
    {
        for (var i = 0; i < 20; i++)
        {
            await InsertAsync(NewErasure());
        }

        var results = await Task.WhenAll(
            _repository.ClaimDueBatchAsync("instance-a", Now, Lease, 20),
            _repository.ClaimDueBatchAsync("instance-b", Now, Lease, 20));

        var claimed = results.SelectMany(batch => batch).ToList();
        Assert.Equal(20, claimed.Count);
        Assert.Equal(20, claimed.Select(r => r.Id).Distinct().Count());
        Assert.Equal(20, claimed.Select(r => r.LeaseId).Distinct().Count());
    }

    [Fact]
    public async Task Claim_LeasedRequest_IsNotReclaimedUntilTheLeaseExpires()
    {
        await InsertAsync(NewErasure());
        var first = await ClaimOneAsync();

        Assert.Empty(await _repository.ClaimDueBatchAsync("instance-b", Now.AddMinutes(1), Lease, 10));

        var reclaimed = await ClaimOneAsync("instance-b", Now.Add(Lease).AddSeconds(1));
        Assert.Equal("instance-b", reclaimed.ClaimedBy);
        Assert.NotEqual(first.LeaseId, reclaimed.LeaseId);
    }

    [Fact]
    public async Task Claim_AfterRepeatedCrashes_CountsEveryAttempt()
    {
        await InsertAsync(NewErasure());

        var at = Now;
        PrivacyRequest claimed = null!;
        for (var i = 0; i < 3; i++)
        {
            // Never released, as when the worker crashes mid-run.
            claimed = await ClaimOneAsync(at: at);
            at = at.Add(Lease).AddSeconds(1);
        }

        Assert.Equal(3, claimed.Attempts);
    }

    [Fact]
    public async Task StaleWorker_CannotOverwriteTheRequestAfterItWasReclaimed()
    {
        var request = await InsertAsync(NewErasure());
        var stale = await ClaimOneAsync("instance-a");
        var current = await ClaimOneAsync("instance-b", Now.Add(Lease).AddSeconds(1));

        Assert.True(await _repository.CompleteAsync(request.Id, current.LeaseId!, Now));
        Assert.False(await _repository.FailAsync(request.Id, stale.LeaseId!, Now, "late failure"));
        Assert.False(await _repository.MarkRetryAsync(request.Id, stale.LeaseId!, Now.AddHours(1), "late retry"));
        Assert.False(await _repository.RecordResultAsync(request.Id, stale.LeaseId!,
            Result("conversations", PrivacyContributorStatus.Failed)));

        var stored = (await _repository.GetByIdAsync(request.Id))!;
        Assert.Equal(PrivacyRequestStatus.Completed, stored.Status);
        Assert.Null(stored.LastError);
        Assert.Empty(stored.Results);
    }

    [Fact]
    public async Task Retry_WaitsForNextAttemptTime()
    {
        var request = await InsertAsync(NewErasure());
        var claimed = await ClaimOneAsync();

        Assert.True(await _repository.MarkRetryAsync(request.Id, claimed.LeaseId!, Now.AddMinutes(10), "store unavailable"));

        Assert.Empty(await _repository.ClaimDueBatchAsync("instance-a", Now.AddMinutes(1), Lease, 10));
        var retried = await ClaimOneAsync(at: Now.AddMinutes(11));
        Assert.Equal(2, retried.Attempts);
        Assert.Equal("store unavailable", retried.LastError);
    }

    [Fact]
    public async Task RecordResult_ReplacesTheEarlierResultOfTheSameContributor()
    {
        var request = await InsertAsync(NewErasure());
        var leaseId = (await ClaimOneAsync()).LeaseId!;

        var failed = Result("conversations", PrivacyContributorStatus.Failed);
        failed.Error = "timeout";
        await _repository.RecordResultAsync(request.Id, leaseId, failed);
        await _repository.RecordResultAsync(request.Id, leaseId, Result("agent-data", PrivacyContributorStatus.Succeeded, 3));
        await _repository.RecordResultAsync(request.Id, leaseId, Result("conversations", PrivacyContributorStatus.Succeeded, 12));

        var results = (await _repository.GetByIdAsync(request.Id))!.Results;
        Assert.Equal(2, results.Count);
        var conversations = Assert.Single(results, r => r.Name == "conversations");
        Assert.Equal(PrivacyContributorStatus.Succeeded, conversations.Status);
        Assert.Equal(12, conversations.ItemsAffected);
        Assert.Null(conversations.Error);
    }

    [Fact]
    public async Task RecordResult_StoresAnErrorThatLooksLikeAFieldPath()
    {
        var request = await InsertAsync(NewErasure());
        var leaseId = (await ClaimOneAsync()).LeaseId!;
        var failed = Result("conversations", PrivacyContributorStatus.Failed);
        failed.Error = "$results.name";

        await _repository.RecordResultAsync(request.Id, leaseId, failed);

        Assert.Equal("$results.name", Assert.Single((await _repository.GetByIdAsync(request.Id))!.Results).Error);
    }

    [Fact]
    public async Task Complete_ScrubsIdentifiers_KeepingHashScopeAndResults()
    {
        var request = await InsertAsync(NewErasure());
        var leaseId = (await ClaimOneAsync()).LeaseId!;
        await _repository.SaveResolvedSubjectAsync(request.Id, leaseId, new PrivacySubject
        {
            UserIds = ["keycloak|alice"],
            Emails = ["alice@example.com"],
            ParticipantIds = ["alice@example.com"]
        }, "hash-of-primary");
        await _repository.RecordResultAsync(request.Id, leaseId, Result("conversations", PrivacyContributorStatus.Succeeded, 4));
        await _repository.AddNotesAsync(request.Id, leaseId, ["1 agent left without an owner"]);

        Assert.True(await _repository.CompleteAsync(request.Id, leaseId, Now));

        var stored = (await _repository.GetByIdAsync(request.Id))!;
        Assert.Equal(PrivacyRequestStatus.Completed, stored.Status);
        Assert.Equal(Now, stored.CompletedAt);
        Assert.Equal(Now, stored.ClosedAt);
        Assert.Null(stored.Subject.Value);
        Assert.Null(stored.ResolvedSubject);
        Assert.Null(stored.RequestedBy.UserId);
        Assert.Equal(PrivacyActorType.Self, stored.RequestedBy.ActorType);
        Assert.Equal("hash-of-primary", stored.SubjectHash);
        Assert.Equal(["t1"], stored.Scope.TenantIds);
        Assert.Single(stored.Results);
        Assert.Equal(["1 agent left without an owner"], stored.Notes);
        Assert.Equal([request.Id], (await _repository.ListByRequesterAsync(Hash("keycloak|alice"), 50)).Select(r => r.Id));

        var raw = await _collection.Database.GetCollection<BsonDocument>("privacy_requests")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(request.Id))).SingleAsync();
        Assert.DoesNotContain("alice", raw.ToJson());
    }

    [Fact]
    public async Task Complete_KeepsAnAdminRequester()
    {
        var request = await InsertAsync(NewErasure(requester: "keycloak|admin", actor: PrivacyActorType.TenantAdmin));
        var leaseId = (await ClaimOneAsync()).LeaseId!;

        await _repository.CompleteAsync(request.Id, leaseId, Now);

        Assert.Equal("keycloak|admin", (await _repository.GetByIdAsync(request.Id))!.RequestedBy.UserId);
    }

    [Fact]
    public async Task Fail_ClosesTheRequestAndKeepsIdentifiersForFollowUp()
    {
        var request = await InsertAsync(NewErasure());
        var leaseId = (await ClaimOneAsync()).LeaseId!;

        Assert.True(await _repository.FailAsync(request.Id, leaseId, Now, "contributor kept failing"));

        var stored = (await _repository.GetByIdAsync(request.Id))!;
        Assert.Equal(PrivacyRequestStatus.Failed, stored.Status);
        Assert.Equal(Now, stored.ClosedAt);
        Assert.Equal("keycloak|alice", stored.Subject.Value);
        Assert.Empty(await _repository.ClaimDueBatchAsync("instance-a", Now.AddDays(1), Lease, 10));
    }

    [Fact]
    public async Task Lists_FilterByRequesterTenantAndStatus()
    {
        var own = await InsertAsync(NewErasure(tenants: ["t1", "t2"]));
        var other = await InsertAsync(NewErasure(requester: "keycloak|bob", tenants: ["t2"]));
        await InsertAsync(NewErasure(requester: "keycloak|carol", tenants: ["t3"]));
        await _repository.CancelAsync(other.Id, Hash("keycloak|bob"), Now);

        Assert.Equal([own.Id], (await _repository.ListByRequesterAsync(Hash("keycloak|alice"), 50)).Select(r => r.Id));
        Assert.Equal(2, (await _repository.ListByTenantAsync("t2", null, 50)).Count);
        Assert.Equal([own.Id],
            (await _repository.ListByTenantAsync("t2", [PrivacyRequestStatus.Pending], 50)).Select(r => r.Id));
        Assert.Equal(3, (await _repository.ListAllAsync(null, 50)).Count);
    }
}
