using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Repositories;

namespace Shared.Services.Privacy;

/// <summary>Builds the filter that finds the person's documents in one collection. Null when there is nothing to match.</summary>
internal delegate Task<FilterDefinition<BsonDocument>?> PersonalDataFilter(
    IMongoDatabase database, PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken);

/// <param name="Name">The MongoDB collection.</param>
/// <param name="Filter">Finds the person's documents in the collection.</param>
internal sealed record PersonalDataCollection(string Name, PersonalDataFilter Filter);

/// <param name="Name">The data area, reported as <see cref="IPersonalDataSource.Name"/>.</param>
/// <param name="Collections">The collections of the area, counted in this order.</param>
internal sealed record PersonalDataArea(string Name, IReadOnlyList<PersonalDataCollection> Collections);

/// <summary>Counts the person's documents in each collection of one MongoDB data area.</summary>
internal sealed class MongoPersonalDataSource : IPersonalDataSource
{
    private readonly PersonalDataArea _area;
    private readonly IDatabaseService _databaseService;

    public MongoPersonalDataSource(PersonalDataArea area, IDatabaseService databaseService)
    {
        _area = area ?? throw new ArgumentNullException(nameof(area));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    public string Name => _area.Name;

    public async Task<IReadOnlyList<PersonalDataCount>> CountAsync(
        PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);

        var database = await _databaseService.GetDatabaseAsync();
        var counts = new List<PersonalDataCount>(_area.Collections.Count);
        foreach (var collection in _area.Collections)
        {
            var filter = await collection.Filter(database, subject, scope, cancellationToken);
            var records = filter == null
                ? 0
                : await database.GetCollection<BsonDocument>(collection.Name)
                    .CountDocumentsAsync(filter, cancellationToken: cancellationToken);
            counts.Add(new PersonalDataCount(collection.Name, records));
        }

        return counts;
    }
}

/// <summary>
/// Every MongoDB collection that holds personal data or references a person, grouped by data area. This is the
/// record of processing in code; keep it in step with the table in <c>docs/PRIVACY_REQUESTS.md</c>.
/// </summary>
internal static class MongoPersonalDataSources
{
    private const string TenantId = "tenant_id";

    public static readonly IReadOnlyList<PersonalDataArea> Areas =
    [
        new("conversations",
        [
            new("conversation_thread", Sync((subject, scope) => Threads(subject, scope))),
            new("conversation_message", async (database, subject, scope, cancellationToken) =>
                PrivacyFilters.All(InScope(subject, scope), PrivacyFilters.Either(
                    PrivacyFilters.AnyOf("thread_id", await ThreadIdsAsync(database, subject, scope, cancellationToken)),
                    ByPerson("participant_id", subject)))),
            new("message_feedback", async (database, subject, scope, cancellationToken) =>
                PrivacyFilters.All(InScope(subject, scope), PrivacyFilters.Either(
                    PrivacyFilters.AnyOf("thread_id", await ThreadIdsAsync(database, subject, scope, cancellationToken)),
                    ByPerson("participant_id", subject),
                    ByPerson("submitted_by", subject))))
        ]),
        new("contacts", [new("contacts", Sync(ByEmail))]),
        new("invitations", [new("user_invitations", Sync(ByEmail))]),
        new("documents", [new("documents", Sync((subject, scope) => InScopeByPerson(subject, scope, "participant_id")))]),
        new("activity-and-logs",
        [
            new("logs", async (database, subject, scope, cancellationToken) =>
                PrivacyFilters.All(InScope(subject, scope), PrivacyFilters.Either(
                    ByPerson("participant_id", subject),
                    PrivacyFilters.AnyOf("workflow_id", await OwnWorkflowIdsAsync(database, subject, scope, cancellationToken))))),
            // No tenant or participant field, so only reachable through the person's own workflows.
            new("activity_history", async (database, subject, scope, cancellationToken) =>
                PrivacyFilters.AnyOf("workflow_id", await OwnWorkflowIdsAsync(database, subject, scope, cancellationToken)))
        ]),
        new("usage", [new("usage_metrics", Sync((subject, scope) => InScopeByPerson(subject, scope, "participant_id")))]),
        new("audit", [new("audit_logs", Sync((subject, scope) => InScopeByPerson(subject, scope, "participant_id", "logged_in_user")))]),
        new("webhooks",
        [
            new("webhook_incomings", async (database, subject, scope, cancellationToken) =>
                PrivacyFilters.All(InScope(subject, scope),
                    PrivacyFilters.AnyOf("workflow_id", await OwnWorkflowIdsAsync(database, subject, scope, cancellationToken)))),
            new("webhook_deliveries", Sync((subject, scope) => InScopeByPerson(subject, scope, "actor_user_id")))
        ]),
        new("credentials",
        [
            new("api_keys", Sync((subject, scope) => InScopeByPerson(subject, scope, "participant_id"))),
            // Certificate has no BsonElement attributes, so its fields are stored under their property names.
            new("certificates", Sync((subject, scope) => PrivacyFilters.All(
                PrivacyFilters.InScope("TenantId", subject, scope), PrivacyFilters.AnyOf("IssuedTo", subject.UserIds)))),
            new("secret_vault", Sync((subject, scope) => InScopeByPerson(subject, scope, "user_id")))
        ]),
        // Tenant records the person created or changed. Agents and flow definitions name the tenant field "tenant".
        new("ownership",
        [
            Authored("agents", "tenant", "created_by", "owner_access", "read_access", "write_access"),
            Authored("activations", TenantId, "created_by", "participant_id"),
            Authored("flow_definitions", "tenant", "created_by"),
            Authored("knowledge", TenantId, "created_by"),
            Authored("app_integrations", TenantId, "created_by", "updated_by"),
            Authored("tenants", TenantId, "created_by"),
            Authored("contacts", TenantId, "created_by", "updated_by"),
            Authored("documents", TenantId, "created_by", "updated_by"),
            Authored("api_keys", TenantId, "created_by"),
            Authored("secret_vault", TenantId, "created_by", "updated_by"),
            Authored("conversation_thread", TenantId, "created_by"),
            Authored("conversation_message", TenantId, "created_by"),
            Authored("tenant_oidc_config", TenantId, "created_by", "updated_by"),
            Authored("tenant_smtp_config", TenantId, "created_by", "updated_by"),
            Authored("tenant_temporal_config", TenantId, "created_by", "updated_by", "reverted_by"),
            // The capability matrix is platform-wide and has no tenant field, so only all-tenant requests reach it.
            Authored("admin_capability_matrix", TenantId, "updated_by"),
            // Health checks also name the person's contacts as the people to notify.
            new("agent_health_checks", async (database, subject, scope, cancellationToken) =>
                PrivacyFilters.All(InScope(subject, scope), PrivacyFilters.Either(
                    ByPerson("created_by", subject),
                    ByPerson("updated_by", subject),
                    PrivacyFilters.AnyOf("notify_contact_ids", await ContactIdsAsync(database, subject, scope, cancellationToken)))))
        ]),
        new("account", [new("users", Sync(Accounts))])
    ];

    private static PersonalDataFilter Sync(Func<PrivacySubject, PrivacyScope, FilterDefinition<BsonDocument>?> filter) =>
        (_, subject, scope, _) => Task.FromResult(filter(subject, scope));

    private static FilterDefinition<BsonDocument>? InScope(PrivacySubject subject, PrivacyScope scope) =>
        PrivacyFilters.InScope(TenantId, subject, scope);

    private static FilterDefinition<BsonDocument>? ByPerson(string field, PrivacySubject subject) =>
        PrivacyFilters.AnyOf(field, PrivacyFilters.PersonIds(subject));

    private static FilterDefinition<BsonDocument>? InScopeByPerson(PrivacySubject subject, PrivacyScope scope, params string[] fields) =>
        PrivacyFilters.All(InScope(subject, scope), PrivacyFilters.Either(fields.Select(field => ByPerson(field, subject)).ToArray()));

    // Contact and invitation emails are stored as entered, so they are matched ignoring case.
    private static FilterDefinition<BsonDocument>? ByEmail(PrivacySubject subject, PrivacyScope scope) =>
        PrivacyFilters.All(InScope(subject, scope), EmailFilter.MatchAnyIgnoringCase<BsonDocument>("email", subject.Emails));

    private static PersonalDataCollection Authored(string collection, string tenantField, params string[] personFields) =>
        new(collection, Sync((subject, scope) => PrivacyFilters.All(
            PrivacyFilters.InScope(tenantField, subject, scope),
            PrivacyFilters.Either(personFields.Select(field => ByPerson(field, subject)).ToArray()))));

    private static FilterDefinition<BsonDocument>? Threads(PrivacySubject subject, PrivacyScope scope) =>
        PrivacyFilters.All(InScope(subject, scope), ByPerson("participant_id", subject));

    // Messages and feedback store the thread's _id as a string.
    private static async Task<List<string>> ThreadIdsAsync(
        IMongoDatabase database, PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken)
    {
        var filter = Threads(subject, scope);
        if (filter == null)
        {
            return [];
        }

        var threads = await database.GetCollection<BsonDocument>("conversation_thread")
            .Find(filter)
            .Project(Builders<BsonDocument>.Projection.Include("_id"))
            .ToListAsync(cancellationToken);
        return threads.Select(thread => thread["_id"].ToString()!).ToList();
    }

    // Health checks store the contact's _id as a string.
    private static async Task<List<string>> ContactIdsAsync(
        IMongoDatabase database, PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken)
    {
        var filter = ByEmail(subject, scope);
        if (filter == null)
        {
            return [];
        }

        var contacts = await database.GetCollection<BsonDocument>("contacts")
            .Find(filter)
            .Project(Builders<BsonDocument>.Projection.Include("_id"))
            .ToListAsync(cancellationToken);
        return contacts.Select(contact => contact["_id"].ToString()!).ToList();
    }

    /// <summary>
    /// The person's conversation workflows that no other participant has a thread in within the scope. A workflow
    /// can hold several participants' threads, so records keyed only by workflow ID are the person's only for these.
    /// </summary>
    internal static async Task<List<string>> OwnWorkflowIdsAsync(
        IMongoDatabase database, PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken)
    {
        var workflowIds = subject.WorkflowIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        var inScope = InScope(subject, scope);
        if (workflowIds.Count == 0 || inScope == null)
        {
            return [];
        }

        var sharedFilter = inScope
            & Builders<BsonDocument>.Filter.In("workflow_id", workflowIds)
            & Builders<BsonDocument>.Filter.Nin("participant_id", PrivacyFilters.PersonIds(subject));
        var shared = await database.GetCollection<BsonDocument>("conversation_thread")
            .Distinct<string>("workflow_id", sharedFilter, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        workflowIds.ExceptWith(shared);
        return workflowIds.Order(StringComparer.Ordinal).ToList();
    }

    // Accounts have no tenant field. A request covering all tenants reaches the accounts themselves; a request
    // limited to some tenants reaches only accounts with a membership in them.
    private static FilterDefinition<BsonDocument>? Accounts(PrivacySubject subject, PrivacyScope scope)
    {
        var accounts = PrivacyFilters.AnyOf("user_id", subject.UserIds);
        if (scope.AllTenants)
        {
            return accounts;
        }

        var memberships = subject.MembershipTenantIds.Intersect(subject.TenantIds, StringComparer.Ordinal);
        return PrivacyFilters.All(accounts, PrivacyFilters.AnyOf("tenant_roles.tenant", memberships));
    }
}
