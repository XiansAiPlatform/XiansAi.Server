using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

public enum PrivacyRequestType
{
    Export,
    Erasure
}

/// <summary>
/// Pending, then Running, then Completed. Cancelled and Failed are the other terminal states.
/// </summary>
public enum PrivacyRequestStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed
}

/// <summary>What kind of identifier the requester named the person by.</summary>
public enum PrivacySubjectKind
{
    UserId,
    Email,
    ParticipantId
}

public enum PrivacyActorType
{
    Self,
    TenantAdmin,
    SysAdmin
}

public enum PrivacyContributorStatus
{
    Succeeded,
    Failed
}

/// <summary>The identifier as the requester gave it. <see cref="Value"/> is scrubbed when the request completes or is cancelled.</summary>
public class PrivacySubjectInput
{
    [BsonElement("value")]
    public string? Value { get; set; }

    [BsonElement("kind")]
    [BsonRepresentation(BsonType.String)]
    public PrivacySubjectKind Kind { get; set; }
}

/// <summary>
/// Which data a request covers.
/// </summary>
public class PrivacyScope
{
    /// <summary>
    /// Tenants the request covers, fixed when the request is created. Tenant admins list requests by
    /// this field, so a request appears to every tenant named here.
    /// </summary>
    [BsonElement("tenant_ids")]
    public List<string> TenantIds { get; set; } = [];

    /// <summary>
    /// Widens resolution to every tenant on the platform, so data in tenants the person has left is
    /// also found. Used by a user's "everything" choice and by SysAdmin erasures.
    /// </summary>
    [BsonElement("all_tenants")]
    public bool AllTenants { get; set; }

    /// <summary>Whether the account itself is deleted. Tenant-scoped erasures never delete the account.</summary>
    [BsonElement("include_account")]
    public bool IncludeAccount { get; set; }
}

public class PrivacyRequester
{
    /// <summary>The requester's user ID. Scrubbed when a self-service request closes, because the requester is the subject.</summary>
    [BsonElement("user_id")]
    public string? UserId { get; set; }

    /// <summary>
    /// Pseudonymised <see cref="UserId"/>. Never scrubbed, so requesters can still list and recognise their own
    /// requests after the plain ID is gone.
    /// </summary>
    [BsonElement("user_hash")]
    public required string UserHash { get; set; }

    [BsonElement("actor_type")]
    [BsonRepresentation(BsonType.String)]
    public PrivacyActorType ActorType { get; set; }
}

/// <summary>Every identifier the person has within the request's scope, captured once and reused on retries.</summary>
public class PrivacySubject
{
    [BsonElement("user_ids")]
    public List<string> UserIds { get; set; } = [];

    [BsonElement("emails")]
    public List<string> Emails { get; set; } = [];

    /// <summary>Conversation participant IDs, including legacy and bare identifiers. All lowercase.</summary>
    [BsonElement("participant_ids")]
    public List<string> ParticipantIds { get; set; } = [];

    /// <summary>Tenants searched for the person's data.</summary>
    [BsonElement("tenant_ids")]
    public List<string> TenantIds { get; set; } = [];

    /// <summary>Tenants within the scope where an account holds a membership.</summary>
    [BsonElement("membership_tenant_ids")]
    public List<string> MembershipTenantIds { get; set; } = [];

    /// <summary>
    /// Conversation workflow IDs, taken from threads before they are deleted, so Temporal erasure
    /// does not depend on the threads still existing.
    /// </summary>
    [BsonElement("workflow_ids")]
    public List<string> WorkflowIds { get; set; } = [];

    [BsonIgnore]
    public bool HasAccount => UserIds.Count > 0;
}

public class PrivacyContributorResult
{
    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public PrivacyContributorStatus Status { get; set; }

    [BsonElement("items_affected")]
    public long ItemsAffected { get; set; }

    /// <summary>Failure detail. Must not contain the person's identifiers, because it is kept after scrubbing.</summary>
    [BsonElement("error")]
    public string? Error { get; set; }

    [BsonElement("completed_at")]
    public DateTime CompletedAt { get; set; }
}

/// <summary>
/// One data subject request (collection <c>privacy_requests</c>). It is the job queue for erasures, the
/// evidence that requests were handled (GDPR Art. 5(2)) and the source of the request lists. It never
/// holds the exported or erased data itself.
/// </summary>
[BsonIgnoreExtraElements]
public class PrivacyRequest
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public PrivacyRequestType Type { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public PrivacyRequestStatus Status { get; set; } = PrivacyRequestStatus.Pending;

    [BsonElement("subject")]
    public required PrivacySubjectInput Subject { get; set; }

    [BsonElement("scope")]
    public required PrivacyScope Scope { get; set; }

    [BsonElement("requested_by")]
    public required PrivacyRequester RequestedBy { get; set; }

    /// <summary>HMAC of the subject's primary identifier. Kept after the plain identifiers are scrubbed.</summary>
    [BsonElement("subject_hash")]
    public required string SubjectHash { get; set; }

    [BsonElement("resolved_subject")]
    public PrivacySubject? ResolvedSubject { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Earliest time the request may run. Creation time plus the erasure delay for erasures.</summary>
    [BsonElement("run_after")]
    public DateTime RunAfter { get; set; }

    [BsonElement("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("cancelled_at")]
    public DateTime? CancelledAt { get; set; }

    /// <summary>When the request reached any terminal state. The retention TTL is on this field.</summary>
    [BsonElement("closed_at")]
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// When the request failed. A shorter TTL is on this field, because a failed request keeps plain identifiers.
    /// </summary>
    [BsonElement("failed_at")]
    public DateTime? FailedAt { get; set; }

    [BsonElement("claimed_by")]
    public string? ClaimedBy { get; set; }

    [BsonElement("lease_expires_at")]
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>
    /// New for every claim. Writes made while processing must carry it, so a worker whose lease expired
    /// and was taken over cannot overwrite the new holder's work.
    /// </summary>
    [BsonElement("lease_id")]
    public string? LeaseId { get; set; }

    /// <summary>How many times the request has been claimed, counted at the claim so a crash still counts.</summary>
    [BsonElement("attempts")]
    public int Attempts { get; set; }

    [BsonElement("next_attempt_at")]
    public DateTime? NextAttemptAt { get; set; }

    [BsonElement("last_error")]
    public string? LastError { get; set; }

    [BsonElement("results")]
    public List<PrivacyContributorResult> Results { get; set; } = [];

    /// <summary>Reports for operators, such as agents left without an owner. Must not contain the person's identifiers.</summary>
    [BsonElement("notes")]
    public List<string> Notes { get; set; } = [];
}
