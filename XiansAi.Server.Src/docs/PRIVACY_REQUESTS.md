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
| `IPersonalDataSource` | Counts a person's records in one data area |
| `IPersonalDataDiscovery` | Lists every data source holding a person's data, with record counts |
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
| Timing | `created_at`, `run_after`, `completed_at`, `cancelled_at`, `closed_at`, `failed_at` |
| Processing | `claimed_by`, `lease_id`, `lease_expires_at`, `attempts`, `next_attempt_at`, `last_error` |
| `resolved_subject` | The identifiers found by the resolver |
| `results` | One result per contributor, with its notes for operators |
| `subject_hash` | HMAC of the person's primary identifier |

```text
Pending ──claim──▶ Running ──complete──▶ Completed
   │                  │
   └──cancel──▶ Cancelled      └──fail──▶ Failed
```

`Completed`, `Cancelled` and `Failed` are closed states and set `closed_at`. `Failed` also sets `failed_at`.

### Repository behaviour

- **Cancel** succeeds only while the request is `Pending`, and only for the requester, matched by
  `requested_by.user_hash`.
- **Claim** atomically takes due erasures (`run_after` passed, not leased or lease expired, retry time
  passed), marks them `Running`, sets a lease, gives each claim a new `lease_id` and counts the attempt.
  Several server instances can claim at once without taking the same request. The claim is not wrapped in
  the application retry helper, because repeating it after a lost reply would claim a second request.
- **Writes made while processing** (resolved subject, contributor results, retry, complete, fail)
  must carry the claim's `lease_id` and return `false` when it no longer matches. A holder whose lease
  expired and was claimed again cannot overwrite the new holder's work. **Retry** clears `lease_id` as well, so
  the worker that scheduled it no longer holds the request.
- **Record result** replaces the contributor's earlier result, notes included, so repeating it never
  duplicates notes.
- **Complete** and **cancel** each scrub the plain identifiers in a single update (see below).
- **Fail** keeps the identifiers and sets `failed_at`, which starts the shorter retention below.
- **Record export delivered** stores an `Export` request that is already `Completed` and scrubbed.
- **Create** is safe to repeat with the same request after an unclear failure.

### What is kept after a request closes

| | Completed | Cancelled | Failed |
|---|---|---|---|
| `subject.value`, `resolved_subject` | scrubbed | scrubbed | kept for 90 days for follow-up |
| `requested_by.user_id` of a `Self` requester | scrubbed | scrubbed | kept for 90 days for follow-up |
| `requested_by.user_id` of an admin requester | kept | kept | kept |
| `subject_hash`, `requested_by.user_hash` | kept | kept | kept |

`requested_by.user_hash` is never scrubbed, so a requester can still list their own requests by it after
their plain user ID is removed.

### Retention

Records are deleted 3 years after `closed_at` by the TTL index `privacy_request_ttl`, as evidence that
requests were handled (GDPR Art. 5(2)).

Failed requests still hold plain identifiers, so they are deleted 90 days after `failed_at` by
`privacy_request_failed_ttl` (storage limitation, Art. 5(1)(e)). Operations must follow up within that window.

Override either per deployment with `MongoIndexes__privacy_requests__{index name}__ExpireAfter` (see
[INDEXES_AND_TTL.md](INDEXES_AND_TTL.md)).

### Stored errors

`last_error` and each result's `error` are kept on the record, so they must not contain identifiers. Build
them with `PrivacyErrorText.Describe`. It keeps the message of a `PrivacyRequestException`, whose messages are
written without identifiers, and only the exception type of anything else, because driver and framework
messages can quote the values they failed on.

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

Resolution throws `PrivacyRequestException` when any resolved participant ID is shared by many senders:
`webhook` and `unknown` (the webhook, Slack and Teams fallbacks) or a `default_participant_id` configured on an
integration in the scoped tenants, enabled or not. Threads under these IDs hold other people's messages, so they
are handled manually.

## Implementing a contributor

Implement `IPersonalDataContributor` (`Shared/Services/Privacy/IPersonalDataContributor.cs`) and register it
in DI as `IPersonalDataContributor`:

- `Name` is stored on request results. Never rename a contributor once it has run.
- `Order` sets the run order, lower first. The account contributor runs last, so the account exists while
  the others act.
- `EraseAsync` must be idempotent, because erasure may run again after a partial failure. Report failure by
  throwing. Only a `PrivacyRequestException` message is stored, other exceptions are stored by type (see
  [Stored errors](#stored-errors)). Return the number of items affected and any notes. Notes must not contain
  the person's identifiers.
- `ExportAsync` writes entries through `IExportWriter`.
- Find records with the same filters as the data area's entry in `MongoPersonalDataSources`, so export and
  erasure cover exactly what [discovery](#discovery) counted.

## Discovery

`IPersonalDataDiscovery.DiscoverAsync(subject, scope)` asks every registered `IPersonalDataSource` for its
counts and returns one row per source and collection with the number of the person's records. Rows with no
records are included, so the result lists every place that was checked. Nothing is changed.

The MongoDB sources are listed in `MongoPersonalDataSources`, one `PersonalDataArea` per data area, each with
the filter for every collection. Filters are built with `PrivacyFilters`:

- `InScope` limits a query to the request's tenants. A request covering all tenants also reaches documents with
  no tenant.
- `AnyOf` matches a field against a list of values. `PersonIds` gives the person's participant IDs, user IDs and
  provider subjects.
- Every builder returns null instead of a filter built from an empty list, and a null filter skips the query.
  An empty filter matches every document, so it would report, and later erase, other people's records.

To add a data source, add a collection to `MongoPersonalDataSources` or register another `IPersonalDataSource`
(such as one for Temporal) in `AddPrivacyServices`. Discovery only uses the interface, so nothing else changes.
Add the store to the record of processing below.

## Record of processing

Every MongoDB collection in the server, classified for the GDPR record of processing (Art. 30). **Personal**
collections hold data about the person. **Reference** collections belong to the tenant and record who created,
changed or can use something. **None** collections hold no personal data.

| Collection | Kind | Personal fields | Found by | Source |
|---|---|---|---|---|
| `conversation_thread` | Personal | `participant_id`, `created_by` | `participant_id` | `conversations`, `ownership` |
| `conversation_message` | Personal | `participant_id`, `text` (encrypted), `created_by` | `thread_id` of the person's threads, `participant_id` | `conversations`, `ownership` |
| `message_feedback` | Personal | `participant_id`, `submitted_by`, `comment` | `thread_id`, `participant_id`, `submitted_by` | `conversations` |
| `contacts` | Personal | `email`, `name`, `phone`, `created_by`, `updated_by` | `email` (ignoring case) | `contacts`, `ownership` |
| `user_invitations` | Personal | `email` | `email` (ignoring case), every status | `invitations` |
| `documents` | Personal | `participant_id`, `content`, `created_by`, `updated_by` | `participant_id` | `documents`, `ownership` |
| `logs` | Personal | `participant_id`, `message`, `workflow_id` | `participant_id`, the person's own workflows | `activity-and-logs` |
| `activity_history` | Personal | `inputs`, `result` | The person's own workflows (no tenant field) | `activity-and-logs` |
| `usage_metrics` | Personal | `participant_id` | `participant_id` | `usage` |
| `audit_logs` | Personal | `participant_id`, `logged_in_user` | Either field | `audit` |
| `webhook_incomings` | Personal | `body`, `headers_encrypted` | The person's own workflows | `webhooks` |
| `webhook_deliveries` | Personal | `actor_user_id`, `payload` | `actor_user_id` | `webhooks` |
| `users` | Personal | `user_id`, `email`, `name`, `tenant_roles` | `user_id`; with a tenant scope, only accounts with a membership in it | `account` |
| `api_keys` | Reference | `participant_id`, `created_by` | Either field | `credentials`, `ownership` |
| `certificates` | Reference | `IssuedTo` | `IssuedTo` (user ID) | `credentials` |
| `secret_vault` | Reference | `user_id`, `created_by`, `updated_by` | Any of the fields | `credentials`, `ownership` |
| `agents` | Reference | `created_by`, `owner_access`, `read_access`, `write_access` | Any of the fields | `ownership` |
| `activations` | Reference | `created_by`, `participant_id` | Either field | `ownership` |
| `flow_definitions` | Reference | `created_by` | `created_by` | `ownership` |
| `knowledge` | Reference | `created_by` | `created_by` | `ownership` |
| `app_integrations` | Reference | `created_by`, `updated_by` | Either field | `ownership` |
| `tenants` | Reference | `created_by` | `created_by` | `ownership` |
| `tenant_oidc_config` | Reference | `created_by`, `updated_by` | Either field | `ownership` |
| `tenant_smtp_config` | Reference | `created_by`, `updated_by` | Either field | `ownership` |
| `tenant_temporal_config` | Reference | `created_by`, `updated_by`, `reverted_by` | Any of the fields | `ownership` |
| `admin_capability_matrix` | Reference | `updated_by` | `updated_by`; platform-wide, so only all-tenant requests | `ownership` |
| `agent_health_checks` | Reference | `created_by`, `updated_by`, `notify_contact_ids` | Either person field, or the `_id` of the person's contacts | `ownership` |
| `secret_vault_values` | Reference | Secret values, keyed by `secret_vault._id` | Through `secret_vault` | |
| `agent_health_check_runs` | None | Results of scripted checks, keyed by `check_id` | | |
| `processed_events` | None | | | |
| `privacy_requests` | None | The request records themselves | | Own retention, see [What is kept](#what-is-kept-after-a-request-closes) |

Rules that apply across the table:

- **Own workflows.** A conversation workflow can hold several participants' threads. `activity_history`,
  `webhook_incomings` and workflow-keyed `logs` are counted only for the person's workflows that no other
  participant has a thread in within the scope.
- **Account.** A request covering all tenants counts the person's accounts. A request limited to some tenants
  counts only accounts with a membership in those tenants.
- **Free text.** Names or emails typed into free text, such as audit `details`, knowledge content or
  agent-written documents, cannot be found by identifier.

### Other stores

- **Temporal workflow history** is not covered yet. It gets its own `IPersonalDataSource` and contributor in epic item 2.
- **Blob storage and vector stores:** the server uses none. `documents` and `knowledge` serve as the knowledge
  base and are covered above.

## Indexes

| Index | Keys | Used by |
|---|---|---|
| `privacy_request_claim` | `status`, `run_after` | Claiming due erasures |
| `privacy_request_tenant` | `scope.tenant_ids`, `created_at`, `status` | Listing a tenant's requests, with or without a status filter |
| `privacy_request_requester` | `requested_by.user_hash`, `created_at` | Listing a requester's own requests |
| `privacy_request_ttl` | `closed_at`, expires after 1095 days | Record retention |
| `privacy_request_failed_ttl` | `failed_at`, expires after 90 days | Retention of failed requests |
| `conversation_thread.thread_participant_lookup` | `participant_id`, `tenant_id`, `workflow_id` | Finding a person's threads and their workflow IDs from the index alone |
| `activity_history.activity_history_workflow_lookup` | `workflow_id` | Finding activity history of the person's own workflows (no tenant field) |
| `documents.document_participant_lookup` | `participant_id`, `tenant_id` | Finding a person's documents |
| `api_keys.api_key_participant_lookup` | `participant_id`, `tenant_id` | Finding a person's API keys |
| `message_feedback.feedback_thread_lookup` | `thread_id`, `tenant_id` | Finding feedback in the person's threads |
| `message_feedback.feedback_participant_lookup` | `participant_id`, `tenant_id` | Finding a person's feedback |
| `message_feedback.feedback_submitter_lookup` | `submitted_by`, `tenant_id` | Finding feedback the person submitted |

## Implementation

| Part | File |
|---|---|
| Options | `Shared/Configuration/Options/PrivacyOptions.cs` |
| Registration | `SharedServices.AddPrivacyServices` in `Shared/Configuration/SharedServices.cs` |
| Request model | `Shared/Data/Models/PrivacyRequest.cs` |
| Repository | `Shared/Repositories/PrivacyRequestRepository.cs` |
| Identifier resolution | `Shared/Services/Privacy/PrivacySubjectResolver.cs` |
| Contributor and export writer contracts | `Shared/Services/Privacy/IPersonalDataContributor.cs` |
| Data source contract | `Shared/Services/Privacy/IPersonalDataSource.cs` |
| Discovery | `Shared/Services/Privacy/PersonalDataDiscovery.cs` |
| MongoDB sources | `Shared/Services/Privacy/MongoPersonalDataSources.cs` |
| Filter helpers | `Shared/Services/Privacy/PrivacyFilters.cs` |
| HMAC hashing | `Shared/Services/Privacy/Pseudonymiser.cs` |
| Storable errors | `Shared/Services/Privacy/PrivacyRequestException.cs` |
