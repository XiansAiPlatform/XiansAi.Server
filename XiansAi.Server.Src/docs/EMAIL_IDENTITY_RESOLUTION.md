# Email Identity Resolution Across Providers

How the server decides *who* a caller is, and *what authority they carry*, when the same email
address belongs to more than one user record.

This situation is normal rather than exceptional: a user record is keyed on the identity provider's
subject, and a subject is only unique within one issuer. One person signing in through two
directories — two Azure B2C tenants, an Entra directory and a Google workspace, a corporate IdP and
a customer-facing one — legitimately has two records carrying the same address.

## What identifies an account

| Field | Meaning |
| --- | --- |
| `UserId` | The provider's subject (`sub` / `oid`, or the configured `userIdClaim`). The collection is keyed on this. Unique only within one issuer. |
| `ProviderAuthority` | The normalized OIDC authority whose signing keys authenticated this subject. Pins the record to one provider so a second provider cannot claim the same subject. Null on records created before pinning existed; those are pinned on first use. |
| `Email` | Stored lowercase. Used for display, for contact, and — on the credentials listed below — for identifying the caller. Not unique. |

`UserId` is the identity. `Email` is a label that usually happens to be unique and sometimes is not.
Everything in this document follows from that distinction.

### The provider pin

On every sign-in, `UserTenantService.IsSameProviderAsync` compares the record's `ProviderAuthority`
against the authority that actually served the discovery document for this token:

- **Pinned and matching** — the sign-in proceeds.
- **Pinned and different** — refused with "This subject is registered to a different identity
  provider". Without this, a second provider asserting the same subject string would resolve to this
  person's record.
- **Not pinned** — the record adopts the presented authority via `PinProviderAuthorityIfUnsetAsync`,
  which is a conditional write. If a concurrent sign-in pinned it to something else first, the loser
  is refused.

The authority is used rather than the token's `iss` claim deliberately: the expected issuer comes
from tenant-supplied configuration and can name any string, whereas the authority must actually
serve the discovery document, so it cannot be pointed at a provider the configurer does not control.

## When a second record with the same address is created

All of this happens in `UserManagementService.CreateNewUser`, which every provisioning path funnels
through. `AdmitEmailAsync` decides the outcome.

| Situation | Outcome |
| --- | --- |
| Nobody holds the address | Created normally. |
| Held at the **same** provider | **Refused** — `A user with this email already exists`. Within one directory the address really does name one account, so a second record is a duplicate. |
| Either side has **no** `ProviderAuthority` | **Refused**. Records that cannot be told apart have to be assumed to be the same account, or an unpinned record would be a way around the rule. |
| Held at a **different** provider by an ordinary account | Created normally. This is the case the design exists for. |
| Held at a **different** provider by a **system administrator** | Created **disabled**, with `IsSysAdmin` false and a `LockedOutReason` explaining what to do. See [System administrators](#system-administrators-across-providers). |
| The new record has **no** address | Created, with a warning. Blank addresses are not compared — comparing them would make every address-less record collide with the first. |

The same-provider check runs first, so a duplicate inside one directory is refused for being a
duplicate rather than queued for a review that should never happen.

## Which credentials resolve by address

Most of the system never touches any of this, because most credentials name the account directly.
Address-based resolution only happens where the credential carries nothing else.

| Path | What the credential names | How it resolves |
| --- | --- | --- |
| Admin API key, modern | The canonical `UserId` | Direct lookup, roles served from `RoleCacheService`. |
| Admin API key, legacy `CreatedBy` | An email | Folded (below). Roles come back with the identity and are **not** read from the cache. Participant roles are dropped. |
| Agent API certificate, OU is a user id | The `UserId` | `CertificateUserAccess.Resolve`, which applies the same rules as the fold to that one record. |
| Agent API certificate, OU is an email | An email | Folded. |
| User API sign-in | The provider subject | Direct lookup. Never folded — a token names its subject exactly. |
| WebAPI console sign-in | Whatever `ClaimMappings.UserIdClaim` names, `sub` by default | Direct lookup. Folded only where that claim is configured to an address, since then the token names one no more exactly than a legacy API key does. |
| Admin participant lookup, `/participants/{email}` | An email | Folded across every tenant, since the reply names no account. |
| Admin participant lookup, `/participants/by-user-id/{userId}` | The `UserId` | Direct lookup. Nothing is combined: the caller named one account, so the reply describes that one. |
| Admin ownership transfer, tenant participant create | Either | **Refused** on an ambiguous address rather than folded. See [Endpoints that refuse](#endpoints-that-refuse-instead-of-folding). |

## The fold

`EmailIdentityResolution.From` turns every record holding an address into the single identity the
credential acts as. Given the address and the tenant the request is for:

1. **Drop every disabled record.** A locked-out account contributes nothing — not its roles, not its
   SysAdmin flag, not its candidacy for being the primary account.
2. **If nothing is left, the result is null** and the caller denies the request.
3. **Order the survivors** by `CreatedAt`, then by `UserId` ordinally. This ordering is the reason a
   credential resolves to the same account on every request instead of to whatever a collection scan
   returned first.
4. **`PrimaryUserId` is the first of them.** This is the account the request acts as, and what its
   data is scoped and attributed to.
5. **Union the roles** of every survivor's *approved* membership of the requested tenant. A
   membership still awaiting approval contributes nothing, so reaching a tenant this way still
   requires one of its admins to have granted the membership.
6. **Decide SysAdmin** — true only when *every* survivor holds it. See below.
7. **`IsAmbiguous`** is true when more than one record contributed, and the fold logs
   `Email {Email} resolves to {Count} accounts`.

The union in step 5 is what makes a shared address usable at all: the person gets the combined
tenant access of their accounts, which is what someone holding a credential that names only their
address would expect.

## System administrators across providers

SysAdmin is global — it grants access to every tenant — so it is never assembled out of records that
disagree about it.

> An address carries SysAdmin only when **every enabled record holding that address** has the role.

One record short and the answer is no, and the fold logs
`Refusing SysAdmin for {Email}: it is held by a system administrator and by {Others}`.

This is not merely a read-time rule. It works because a second record for an administrator's address
is created inert:

### Accepting two accounts as the same administrator

1. The person signs in from the second directory. A record is created **disabled**, with
   `IsSysAdmin` false and a `LockedOutReason` naming the administrator it collides with. While it is
   disabled it is dropped in step 1 of the fold, so the existing administrator is entirely
   unaffected.
2. An operator reviews it. If the two really are the same person, they **enable** the account and
   **grant it SysAdmin** through `PUT /api/v1/admin/users/{userId}/sysadmin`.
3. Both records now hold the role, so the address resolves as SysAdmin again — now from either
   account.

Order does not matter: granting the role while the account is still disabled is harmless, because a
disabled record is dropped before anything else is decided.

### Why only that one endpoint

Every other route to the flag refuses while the address is shared, because none of them represents a
person deciding that these two accounts are the same someone:

- `RoleManagementService` (WebAPI role management) returns a conflict pointing at the global user
  endpoint.
- Azure AD group-claim sync refuses to *grant*, logging `Not granting SysAdmin to {UserId} from
  group claims`. Being in the admin group of a second directory is that directory's decision, not
  this platform's. **Demotion is never blocked** — taking the role away is always the safe
  direction.

### The gap between enabling and granting

If the account is enabled but not yet granted the role, there are two enabled records and they do
not agree, so the address stops carrying SysAdmin. The blast radius is only credentials that name an
address — legacy API keys with an email `CreatedBy`, certificates with an email OU. Anything keyed
by user id reads `IsSysAdmin` off the record and is unaffected, and completing the grant restores
it.

Two things make this visible rather than silent: the `LockedOutReason` written at creation says so
in as many words and is surfaced as `disabledReason` on the admin user detail, and enabling such an
account logs `Enabled {UserId}, which holds the same email as system administrator(s) {Others}`.

## What a disabled account can do

Nothing. `IsLockedOut` is enforced at every door:

- **Sign-in** — `UserTenantService.GetApprovedTenantsForUserId` is the funnel every sign-in path
  reaches its tenants through, and refuses with "This account is disabled". This holds for a
  disabled SysAdmin too, who would otherwise be handed every tenant.
- **Roles by user id** — `UserRepository.GetUserRolesAsync` returns an empty list.
- **The fold** — the record is dropped before anything is computed.
- **Certificates** — `CertificateUserAccess` refuses with "User account is locked out".

## Conversation identity

The User API uses an account's stored email as the `participantId`, which is the namespace its
message threads live in. Two accounts answering to one address would therefore share one namespace,
and either could read the other's conversations.

`UserTenantService.ConversationIdentityEmailAsync` prevents this: when the address is held by more
than one record it is withheld, logging `Not using the email of {UserId} as their conversation
identity`, and the account falls back to being named by its provider subject — which always names
exactly one account.

The address is withheld only from *naming threads*. It is still carried as the account's email, so a
caller is recognised when they name themselves by it: `ParticipantIdResolver` treats any id the
caller answers to — participant id, canonical login id, account email, or provider subject — as a
request for their own threads, and resolves it to the id they were actually issued. Clients that
send an email address as `participantId` therefore keep working when it turns out to be shared,
instead of being refused with a 403.

The practical consequence is that a person whose address is shared gets a *different*
`participantId` than they would with a unique address. Their existing threads under the address are
not visible under the subject.

## Endpoints that refuse instead of folding

The fold answers "what may this credential do", where combining the accounts is the right answer.
An operator naming a person by address is asking something else — *which* account — and there
folding would act on an account they did not mean.

These surfaces count every record holding the address and refuse when more than one does. An
address exactly one account holds names it as surely as a user id would, so that case proceeds:
refusing it would not make anything safer, since an operator sent to find the user id would search
by the same address and arrive at the same record.

- **Create tenant participant** (`TenantParticipantUserService.CreateAsync`) — grants any tenant
  role, up to `TenantAdmin`. `userId` names an account outright and is the way past an ambiguous
  address; otherwise `email` names the single account holding it, or creates one when none does.
  Refuses a disabled account, rather than leaving a grant on a record that cannot sign in.
- **Add user to current tenant** (`UserTenantService.AddTenantToUserIfExist`) — grants an approved
  `TenantUser`, which is console access. Resolves through `EmailAccountLookup.From` and refuses a
  disabled account. WebAPI only.

**Participant lookup** (`AdminParticipantsEndpoints`) used to sit in this list and no longer does.
It is the one address-named surface whose reply names no account: it returns the tenants a person
reaches and their role in each, so there is nothing for the caller to act on with the wrong record.
Refusing it also had no way out — the remedy for two accounts belonging to one administrator is to
enable both and grant the role to both, and this endpoint kept refusing afterwards, leaving deleting
a record as the only escape. It now combines them: tenants are the union, `PrimaryRole` picks the
highest role across the records for each tenant, and `isSystemAdmin` follows
`EmailIdentityResolution.ResolveSysAdmin`. Disabled records are dropped first, so the `403` for a
locked-out account is now reached only when every record holding the address is disabled.

`GET /participants/by-user-id/{userId}` answers the same shape without any of that. A caller holding
the signed-in person's provider subject names one account outright, so nothing is combined and the
reply describes what that account alone reaches — the rule every sign-in path already follows. The
two can differ deliberately: where one record holds SysAdmin and another does not, the address says
no and the user id says whatever that record says. That is the point of naming an account rather
than an address, and it is safe because a squatter arriving at another directory is provisioned
disabled and refused here on that basis, not on the strength of the address. The address route
remains for callers that have no more than an address, so no stored data or existing caller changes.

One surface takes no address at all. It never creates an account, so its target always exists and
the caller has necessarily obtained it from something that returned a user id; accepting an address
would only add a resolution step that can be redundant or wrong, never necessary:

- **Ownership transfer** (`AdminOwnershipEndpoints`) — `400`, "newAdminId must be a user id, not an
  email address." The account is also refused when disabled, and when it holds no approved
  membership of the tenant unless it is a system administrator — the lookup is not tenant-scoped,
  so without that any account in the deployment could be made owner of a tenant's agent.

## Worked examples

**An ordinary person in two directories.** `dana@example.com` has a record in directory A (created
January, `TenantUser` on `acme`) and directory B (created March, `TenantAdmin` on `acme`). A legacy
API key stores `dana@example.com` as `CreatedBy`. The fold keeps both, orders A first, and the
request acts as A's user id with roles `TenantUser, TenantAdmin` on `acme`. Dana's API key works
with the combined access of both accounts, and consistently attributes its writes to A.

**Someone registering an administrator's address.** `admin@corp.com` is a SysAdmin in directory A.
An unrelated person registers the same address in directory B and signs in. A record is created
disabled. The administrator's certificate and legacy API keys keep working with SysAdmin throughout,
because the disabled record is dropped before the flag is decided. The person cannot sign in at all
until an operator reviews the record, and the operator sees exactly whose address it collides with.

**The same administrator, genuinely.** Same as above, except the operator recognises the person,
enables the record and grants it SysAdmin. Between those two actions the address stops carrying
SysAdmin on address-named credentials; afterwards it carries it from either account.

**A duplicate inside one directory.** Two subjects in directory A both claiming `sam@example.com`.
The second is refused outright at creation. Nothing is folded, because nothing was created.

## Diagnosing from the logs

Addresses are redacted in logs, so match on the message text:

| Log line | What it means |
| --- | --- |
| `resolves to {Count} accounts` | An address-named credential was folded. Informational, not a fault. |
| `Refusing SysAdmin for {Email}` | Enabled records disagree about the role. Either finish the grant or disable the extra account. |
| `Creating {UserId} from {Authority} disabled` | A record was created for review; an operator needs to decide. |
| `Refusing to create {UserId} from {Authority}` | Same-provider or unidentifiable-provider duplicate. The person cannot sign in until the conflict is resolved. |
| `Refusing sign-in for {UserId}: the account is disabled` | A disabled account tried to sign in. |
| `Enabled {UserId}, which holds the same email as system administrator(s)` | An administrator has just lost the role on address-named credentials. |
| `Not using the email of {UserId} as their conversation identity` | This account's threads are namespaced by its subject, not its address. |
| `subject is pinned to provider {Pinned} but was asserted by {Presented}` | A provider presented another provider's subject. Always investigate. |

## Disabling an account takes effect immediately

Every API caches the authorization decision it made, and none of those caches re-check whether the
account is still enabled: the User API keeps the approved tenant list, the Agent API keeps the
certificate's user and roles, the Admin API keeps the resolved roles, and validated tokens are kept
for all of them. Each cache is therefore a window during which a disabled account keeps working.

`IUserCacheIndex` records which entries were written for which account, because the keys are built
from what the request presented — a token hash, a certificate thumbprint, a provider authority and
subject — and none of those can be reconstructed from a user id. `IUserAuthorizationInvalidator`
evicts them, and is called wherever an account is disabled, enabled, or has its roles changed.

It also evicts the accounts that share the address, because a credential naming only an address
resolves through all of them: disabling one drops it from the combined roles and withdraws the
administrator role from the rest, so what the others resolve to has changed too.

Two limits are worth knowing:

- **It is per process.** The caches are in-process, so eviction on the instance that served the
  admin request does not reach the others. Their entries expire on their own, which the absolute
  expirations bound: 30 seconds for User API approved tenants
  (`Auth:ApprovedTenantCacheDurationSeconds`), and 5 minutes for tokens
  (`Auth:TokenValidationCacheDurationMinutes`), roles, and certificates
  (`AgentApi:CertificateValidationCacheMaxDurationMinutes`). Five minutes is therefore the worst
  case for a disabled account anywhere in the deployment.
- **It must be a singleton.** The caches are registered scoped. An index owned by one of them would
  be discarded with the request that wrote the entry, leaving the later request with nothing to
  evict — which is exactly how the token cache's own reverse index behaved before this existed.

## Known edges

- **`RoleCacheService` never caches address-shaped keys.** Entries are keyed on the value passed in,
  while every invalidation passes a canonical user id, so an entry keyed on an address would never
  be invalidated and would serve stale roles for the full five minutes. Entries keyed on a user id
  are tracked and evicted as described above.
- **The endpoints that refuse count disabled records too.** They count every record holding the
  address, unlike the fold, which drops disabled ones first. So an administrator's address that has
  a disabled duplicate awaiting review will get a `409` from them even though every other path
  resolves it without difficulty. Use the user id there. Participant lookup no longer behaves this
  way.
- **No record is kept of whether a provider vouched for an address.** An address a provider merely
  asserted admits a second record on exactly the same terms as one it verified, so someone able to
  set an arbitrary address at their own IdP can put a victim's address on a record they control.
  Nothing downstream trusts an address by itself, which is what contains this: the provider pin
  keys identity on the subject, the fold refuses to combine administrator rights across records
  that were not accepted as the same person, and the endpoints above refuse an ambiguous address
  rather than picking. Enforcing verification at provisioning would be the stronger answer, but it
  is not available on Azure AD B2C, which issues no verification claim at all — every B2C sign-in
  would be treated as unverified and refused.

## Related

- [`AUTH_CONFIGURATION.md`](AUTH_CONFIGURATION.md) — provider configuration, the `userIdClaim`
  rules, and how User API tenant membership is approved.
