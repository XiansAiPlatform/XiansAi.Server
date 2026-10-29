# GDPR Privacy Requests

Building blocks for GDPR data subject requests: **export** (right of access, Art. 15) and **erasure**
(right to erasure, Art. 17). They store each request, find every identifier a person's data is kept under,
define how each data area exports and erases its part, and pseudonymise identifiers that are kept after a
request closes.

Everything is off unless `Privacy:Enabled` is `true`.

## Components

| Component | Responsibility |
|---|---|
| `PrivacyOptions` | Settings and the `Privacy:Enabled` switch, validated at startup |
| `PrivacyRequest` | The request record, stored in `privacy_requests` |
| `IPrivacyRequestRepository` | Creates, lists, cancels, claims, updates and closes requests |
| `IPrivacySubjectResolver` | Turns one identifier into every identifier of the person within a scope |
| `IPersonalDataContributor` | Contract for exporting and erasing one data area |
| `IExportWriter` | Contract for writing export entries (JSON and files) |
| `IPseudonymiser` | HMAC-SHA256 of an identifier, used for the hashes kept on a request |

## Configuration

Settings live under the `Privacy` section. Environment variables use `__` in place of `:`.

| Setting | Default | Description |
|---|---|---|
| `Privacy:Enabled` | `false` | Master switch. When false, only `PrivacyOptions` is registered. |
| `Privacy:PseudonymKey` | none | Secret HMAC key, at least 32 bytes. Required when enabled. |
| `Privacy:ErasureDelay` | `7d` | Cancellation window before an erasure is due. Uses the server duration format (`7d`, `12h`, `5d 6h`). |
| `Privacy:PollIntervalSeconds` | `60` | Polling interval, in seconds, for due erasures. |
| `Privacy:MaxAttempts` | `10` | Attempts allowed for an erasure before it is marked `Failed`. |

When enabled, `IPseudonymiser` (singleton), `IPrivacyRequestRepository` and `IPrivacySubjectResolver`
(scoped) are registered by `SharedServices.AddPrivacyServices`.

### Startup checks

- While privacy is **enabled**, startup fails if the key is missing or shorter than 32 bytes, or if any
  other setting is invalid or out of range. A mistyped cancellation window must not go unnoticed.
- While privacy is **disabled**, invalid values are ignored and defaults are used, so a switched-off
  feature never blocks startup. An unreadable `Privacy:Enabled` value still fails, because it is unclear
  whether the feature was meant to be on.

### The pseudonym key

Generate it randomly, for example `openssl rand -base64 48`, and store it with the deployment's other
secrets. Never commit it to `appsettings*.json`.

Do not change the key once requests exist. Hashes made with a new key do not match earlier ones, so new
requests can no longer be matched to earlier records and requesters can no longer find their earlier
requests.

### Example (environment variables)

```bash
Privacy__Enabled=true
Privacy__PseudonymKey=<secret, at least 32 bytes>
Privacy__ErasureDelay=7d
```

## The request record

Collection `privacy_requests`, model `Shared/Data/Models/PrivacyRequest.cs`. The record never holds the
exported or erased data itself.

| Field group | Content |
|---|---|
| `type`, `status` | `Export` or `Erasure`. `Pending`, `Running`, `Completed`, `Cancelled` or `Failed`. |
| `subject` | The identifier as given and its kind (`UserId`, `Email`, `ParticipantId`) |
| `scope` | `tenant_ids` (fixed at creation, used for tenant listing), `all_tenants`, `include_account` |
| `requested_by` | Requester `user_id`, `user_hash` and actor type (`Self`, `TenantAdmin`, `SysAdmin`) |
| Timing | `created_at`, `run_after`, `completed_at`, `cancelled_at`, `closed_at` |
| Processing | `claimed_by`, `lease_id`, `lease_expires_at`, `attempts`, `next_attempt_at`, `last_error` |
| `resolved_subject` | The identifiers found by the resolver |
| `results`, `notes` | One result per contributor, and notes for operators |
| `subject_hash` | HMAC of the person's primary identifier |

```text
Pending ──claim──▶ Running ──complete──▶ Completed
   │                  │
   └──cancel──▶ Cancelled      └──fail──▶ Failed
```

`Completed`, `Cancelled` and `Failed` are closed states and set `closed_at`.

### Repository behaviour

- **Cancel** succeeds only while the request is `Pending`, and only for the requester, matched by
  `requested_by.user_hash`.
- **Claim** atomically takes due erasures (`run_after` passed, not leased or lease expired, retry time
  passed), marks them `Running`, sets a lease, gives each claim a new `lease_id` and counts the attempt.
  Several server instances can claim at once without taking the same request. The claim is not wrapped in
  the application retry helper, because repeating it after a lost reply would claim a second request.
- **Writes made while processing** (resolved subject, contributor results, notes, retry, complete, fail)
  must carry the claim's `lease_id` and return `false` when it no longer matches. A holder whose lease
  expired and was claimed again cannot overwrite the new holder's work.
- **Complete** and **cancel** each scrub the plain identifiers in a single update (see below).
- **Record export delivered** stores an `Export` request that is already `Completed` and scrubbed.
- **Create** is safe to repeat with the same request after an unclear failure.

### What is kept after a request closes

| | Completed | Cancelled | Failed |
|---|---|---|---|
| `subject.value`, `resolved_subject` | scrubbed | scrubbed | kept for follow-up |
| `requested_by.user_id` of a `Self` requester | scrubbed | scrubbed | kept for follow-up |
| `requested_by.user_id` of an admin requester | kept | kept | kept |
| `subject_hash`, `requested_by.user_hash` | kept | kept | kept |

`requested_by.user_hash` is never scrubbed, so a requester can still list their own requests by it after
their plain user ID is removed.

### Retention

Records are deleted 3 years after `closed_at` by the TTL index `privacy_request_ttl`, as evidence that
requests were handled (GDPR Art. 5(2)). Override it per deployment with
`MongoIndexes__privacy_requests__privacy_request_ttl__ExpireAfter` (see [INDEXES_AND_TTL.md](INDEXES_AND_TTL.md)).

## Identifier resolution

One person can appear under several keys. `PrivacySubjectResolver` collects all of them, limited to the
request's scope:

| Identifier | Source |
|---|---|
| User IDs | The account matching a user ID input, or every account holding an email input |
| Emails | Account emails, included only when every account holding the address is part of the request |
| Participant IDs | The email (as above), the provider subject (the part after the first `\|` of a `provider\|subject` user ID) and the lowercased user ID (the legacy participant ID) |
| Tenants searched | The scope's tenants, or every tenant on the platform when `all_tenants` is set |
| Membership tenants | Tenants within the scope where an account holds a membership |
| Workflow IDs | Distinct `conversation_thread.workflow_id` values of those participant IDs |

If no account matches, the input is used as a bare participant ID (Slack, Teams, webhook and API-key
participants). A participant ID input resolves an account only when it equals that account's email. A
request limited to some tenants never resolves data in other tenants.

## Implementing a contributor

Implement `IPersonalDataContributor` (`Shared/Services/Privacy/IPersonalDataContributor.cs`) and register it
in DI as `IPersonalDataContributor`:

- `Name` is stored on request results. Never rename a contributor once it has run.
- `Order` sets the run order, lower first. The account contributor runs last, so the account exists while
  the others act.
- `EraseAsync` must be idempotent, because erasure may run again after a partial failure. Report failure by
  throwing. Return the number of items affected and any notes. Notes and errors must not contain the
  person's identifiers.
- `ExportAsync` writes entries through `IExportWriter`.

## Indexes

| Index | Keys | Used by |
|---|---|---|
| `privacy_request_claim` | `status`, `run_after` | Claiming due erasures |
| `privacy_request_tenant` | `scope.tenant_ids`, `created_at`, `status` | Listing a tenant's requests, with or without a status filter |
| `privacy_request_requester` | `requested_by.user_hash`, `created_at` | Listing a requester's own requests |
| `privacy_request_ttl` | `closed_at`, expires after 1095 days | Record retention |
| `conversation_thread.thread_participant_lookup` | `participant_id`, `tenant_id`, `workflow_id` | Finding a person's threads and their workflow IDs from the index alone |

## Implementation

| Part | File |
|---|---|
| Options | `Shared/Configuration/Options/PrivacyOptions.cs` |
| Registration | `SharedServices.AddPrivacyServices` in `Shared/Configuration/SharedServices.cs` |
| Request model | `Shared/Data/Models/PrivacyRequest.cs` |
| Repository | `Shared/Repositories/PrivacyRequestRepository.cs` |
| Identifier resolution | `Shared/Services/Privacy/PrivacySubjectResolver.cs` |
| Contributor and export writer contracts | `Shared/Services/Privacy/IPersonalDataContributor.cs` |
| HMAC hashing | `Shared/Services/Privacy/Pseudonymiser.cs` |
