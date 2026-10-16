# Design: Admin API key auth for non-bootstrapped System Admins — #583

**Item:** [#583 Admin API Key Auth Failure for Non-Bootstrapped System Admin Users](https://github.com/XiansAiPlatform/XiansAi.Server/issues/583) · **Type:** Bug · **Size:** S–M

**One line:** Admin-key authorization resolves the key owner's roles by an exact match on the single `ApiKey.CreatedBy` string, assuming it equals the owner's current `User.UserId`; when the key was captured in a different identifier *form* than the owner record is keyed on (email vs. raw provider subject vs. canonical `provider|subject`), the lookup returns zero roles and the request is rejected with `401 "User does not have required admin role"`. The fix makes owner→roles resolution identifier-form-agnostic and adds regression coverage for both bootstrapped and OIDC (Visma Connect) admins.

## 1. Context & scope

Admin API endpoints authenticate with an API key (ADR-0005). On every request, `AdminEndpointAuthenticationHandler` resolves the key owner's roles via `AdminRoleTenantResolver.ResolveAsync(apiKey.CreatedBy, …)` and requires `SysAdmin` **or** `TenantAdmin`; absent either, it returns `401 "User does not have required admin role"` (`AdminRoleTenantResolver.cs:107-117`).

The whole chain depends on one unstated invariant: **`ApiKey.CreatedBy` is stored in the same identifier form as the owner's `User.UserId`.** Three code paths populate these two fields with *different* forms:

| Actor | `User.UserId` is… | `ApiKey.CreatedBy` is… | Set where |
|---|---|---|---|
| Bootstrapped admin | **email** (`admin@company.com`) | **email** | `BootstrapService.cs` (UserId = email; key created with `LoggedInUser = email`) |
| OIDC / Visma Connect user | **raw provider subject** (`vconnect-guid`) | **raw provider subject** | `UserTenantService`/`UserManagementService` provision `UserId = ProviderUserId`; `OidcAuthenticationHandler.cs:81` sets `LoggedInUser = validation.ProviderUserId`; WebApi `ApiKeyEndpoints.cs:26-27` uses `LoggedInUser` as `createdBy` |
| OIDC claims principal / re-derivation | — | — | `OidcAuthenticationHandler.cs:88-89` puts the **canonical** `provider\|subject` into `NameIdentifier`/`userId` claims |

So three distinct identifier forms circulate for the *same* person — **email**, **raw subject**, and **canonical `provider\|subject`** — and `CreatedBy` can legitimately hold any of them depending on when and how the key was minted (bootstrap, current OIDC path, or a legacy key predating the Aug‑2026 identity refactor, commit `6cab46e0`).

`AdminRoleTenantResolver.ResolveAsync` (and the `UserRepository.GetUserRolesAsync` it calls) branch on a single heuristic — `userIdOrEmail.Contains('@')`:

- **email branch** → `ResolveEmailIdentityAsync` folds every record holding that address and unions roles (incl. global `SysAdmin`). Robust.
- **non-email branch** → `GetByUserIdAsync(value)` only, then `GetUserRoles` (appends `SysAdmin` when `IsSysAdmin`). **No folding, no fallback.** If `value` is not *exactly* an existing `User.UserId`, it returns an empty list → no admin role → 401.

A bootstrapped admin's key (`CreatedBy` = email) always has the `@` fallback, so it works. A non-bootstrapped admin's key whose `CreatedBy` form diverges from the owner's current `User.UserId` (canonical-vs-raw subject, a migrated/legacy key, or a SysAdmin grant recorded against a different id form) falls into the non-email branch with no fallback and fails — exactly the reported symptom.

- **In scope:** make admin-key owner→roles resolution identifier-form-agnostic (userId / canonical `provider|subject` / email all resolve to the same account and role union); confirm the SysAdmin grant and the key-owner lookup agree on the identifier; regression tests for bootstrapped + OIDC admin keys; a consistency check / optional backfill for existing keys.
- **Out of scope:** redesigning the OIDC identity model or the `ProviderUserId`-vs-`CanonicalUserId` split (tracked separately — see decision D2); any Agent Studio UI change; new capabilities or endpoints; participant-role semantics.

> **Grooming note.** #583 carries `ai-dlc/issue/analyze`, not `groomed`; this design was produced via `@xianix design anyway`. The mechanism and fix below are grounded in the code, but three questions (§7) change the fix's blast radius and need a decision before implementation. The primary root-cause hypothesis should also be confirmed against a *failing* record (§8, V0).

## 2. Requirements & acceptance-criteria traceability

Derived from the issue's "Tasks / Steps to Resolve":

| Req / AC (from issue) | Satisfied by |
|---|---|
| **T1** Inspect API-key middleware/service handling role validation | §4 locates the exact failure point: `AdminRoleTenantResolver.ResolveAsync` non-email branch (`:98-102`) → `UserRepository.GetUserRolesAsync` non-email branch (`:427-446`). |
| **T2** Standardize identity & role lookup to handle both email-based (bootstrapped) and external-provider IDs (Visma Connect) | §6.1 — single `ResolveKeyOwnerRoles` helper routing userId / canonical / email through `GetByUserIdOrEmailAsync` + canonical-subject handling + `EmailIdentityResolution`, replacing the `Contains('@')` fork. |
| **T3** Verify role assignment maps correctly to the resolved user id | §6.2 — key-owner resolution and the SysAdmin grant (`SetSysAdminAsync`, matched on `User.UserId`) are reconciled to the same canonical account; resolver returns `ResolvedUserId` used for `LoggedInUser`. |
| **T4** Add unit/integration tests for API-key authz for both bootstrapped and Visma admins | §8 — unit tests on the resolver/repository for all three identifier forms + locked-out/ambiguous cases; integration tests in `AdminApiKeyEndpointsTests` / `AdminAuthEndpointsTests` for a bootstrapped admin key and an OIDC-provisioned SysAdmin key. |
| **AC** Any System Admin can generate and use admin keys (expected behavior) | §6.1 end-to-end: a SysAdmin whose record carries `IsSysAdmin=true` resolves to `SysAdmin` regardless of whether `CreatedBy` is email, raw subject, or canonical. |
| **AC** No regression for bootstrapped admin keys | §8 keeps the email path green; the new helper is a superset of today's email branch. |

## 3. Architecture conformance

Constraints `ARCH-001..012` (`docs/architecture/constraints.md`, `Status: proposed` but authoritative). This is a **conformance-preserving bug fix** confined to existing auth/identity code.

| Constraint | How satisfied / deviation |
|---|---|
| ARCH-001 feature-slice layout | Edits stay in `Features/AdminApi/Auth/AdminRoleTenantResolver.cs`; no new slice. |
| ARCH-002 shared cross-cutting | Identity/role folding stays in `Shared/Repositories` (`UserRepository`, `EmailIdentityResolution`) and `Shared/Services` (`RoleCacheService`); no feature logic leaks into `Shared`. |
| ARCH-003/004 dependencies flow outward / no cross-feature | No new `Shared→Features` or feature→feature references. The resolver (a Feature) keeps calling `Shared` only. |
| ARCH-005 endpoint-scoped auth policy | Unchanged — Admin endpoints keep `AdminEndpointAuthPolicy`; this fixes *who resolves to admin*, not the policy wiring. |
| ARCH-006 tenant isolation | Preserved — role folding still filters `TenantRoles` by `Tenant == tenantId && IsApproved`; global `SysAdmin` still gated on `IsSysAdmin` and the all-records-agree rule (`EmailIdentityResolution.ResolveSysAdmin`). No new cross-tenant exposure. |
| ARCH-007 repo-only Mongo | All reads stay behind `IUserRepository` / `IApiKeyRepository`; no new `GetCollection<T>` holders. |
| ARCH-010 DI registration | No new services required for the minimal fix (reuses `IUserRepository`, `IRoleCacheService`). A possible backfill job (D1) would register as a hosted service via existing `SharedServices` precedent. |
| **ADR-0005** dual auth strategy | Reaffirmed, not changed: admin keys still gate on explicit `SysAdmin`/`TenantAdmin`; no email-domain grant is reintroduced (the resolver's class comment at `:50-55` explicitly forbids domain matching, and this design keeps that). |

No new ADR is strictly required; the one notable decision (should `User.UserId`/`CreatedBy` standardize on canonical `provider|subject` and migrate — D2) would, if chosen, warrant an ADR amending ADR-0005's identity notes.

## 4. Components & real paths (the failure, located)

**Failure point (read-only trace):**
- `XiansAi.Server.Src/Features/AdminApi/Auth/AdminRoleTenantResolver.cs` — `ResolveAsync`, the `Contains('@')` fork at **`:90-102`**; the `401` at **`:107-117`**. The non-email branch (`:98-102`) calls `RoleCacheService.GetUserRolesAsync` with no fallback for a non-matching, non-email identifier.
- `XiansAi.Server.Src/Shared/Services/RoleCacheService.cs` — `GetUserRolesAsync` (`:38-78`); non-email values flow to the repo and are cached per id.
- `XiansAi.Server.Src/Shared/Repositories/UserRepository.cs` — `GetUserRolesAsync` (`:420-447`): `GetByUserIdAsync` exact match (`:427`), email-only fallback (`:437-441`), else **empty list (`:443`)** ← silent zero-roles for a non-matching subject/canonical id. `GetUserRoles` (`:449-472`) appends `SysAdmin` on `IsSysAdmin` (`:466-469`). `GetByUserIdOrEmailAsync` (`:357-371`) — the existing userId-then-email resolver the fix should generalize.
- `XiansAi.Server.Src/Shared/Repositories/EmailIdentityResolution.cs` — the folding model to reuse: unions approved tenant roles + global `SysAdmin` across records (`:47-88`, `ResolveSysAdmin` `:125-147`).

**Identifier-origin sites (why forms diverge):**
- `XiansAi.Server.Src/Features/WebApi/Auth/OidcAuthenticationHandler.cs` — `LoggedInUser = ProviderUserId` (raw subject) at **`:81`** vs. canonical in claims at **`:88-89`**.
- `XiansAi.Server.Src/Shared/Auth/DynamicOidcValidator.cs:164-167` — `canonical = provider + "|" + subject`; `ProviderUserId = subject`.
- `XiansAi.Server.Src/Features/WebApi/Endpoints/ApiKeyEndpoints.cs:26-27` — `createdBy = tenantContext.LoggedInUser` (raw subject).
- `XiansAi.Server.Src/Features/AdminApi/Services/BootstrapService.cs` — `UserId = email` and bootstrap key `createdBy = email`.
- `XiansAi.Server.Src/Shared/Repositories/UserRepository.cs` — `SetSysAdminAsync` (`:680-691`) matches `x.UserId == userId`; the SysAdmin grant must target the same id form the key owner resolves to (T3).

**Edited by the fix:** `AdminRoleTenantResolver.cs` (replace the fork), `UserRepository.cs` (`GetUserRolesAsync` — generalize non-email fallback to canonical ids / reuse folding), and tests. No model or endpoint changes for the minimal scope.

## 5. Data

No schema change for the minimal fix. Relevant shapes (unchanged):
- `User` (`Shared/Data/Models/User.cs`): `UserId` (keyed, raw subject | email), `Email`, `IsSysAdmin` (global flag), `TenantRoles[] { Tenant, Roles[], IsApproved }`, `IsLockedOut`, `ProviderAuthority`.
- `ApiKey` (`Shared/Data/Models/ApiKey.cs`): `CreatedBy` (owner identifier, any of the three forms), `TenantId`.

If D1 (backfill) is chosen, no new field is added — existing `CreatedBy` values are *normalized* in place to the owner's canonical `User.UserId`, with the resolver still accepting the old form during the rollout window.

## 6. Behaviour / the fix

### 6.1 Identifier-form-agnostic owner→roles resolution
Replace the `Contains('@')` fork with a single resolution that, in order: (a) tries `GetByUserIdAsync(createdBy)` (exact, the common fast path); (b) if `createdBy` is a canonical `provider|subject`, retries on the raw subject (and vice-versa) so a key minted in one form still finds a record keyed in the other; (c) if still unresolved and `createdBy` is an email, folds via `EmailIdentityResolution`. Whatever matches yields the role union (approved `TenantRoles` for the tenant + global `SysAdmin` per the all-records-agree rule) and a canonical `ResolvedUserId`. This collapses the three legitimate forms to one account and removes the silent empty-list path at `UserRepository.cs:443` for admin-key resolution. The resolver's existing anti-pattern guard (no email-*domain* matching, `:50-55`) is retained — we fold only exact-address records, never a domain.

Net effect: a non-bootstrapped SysAdmin (record `IsSysAdmin=true`) authenticates successfully whether `CreatedBy` is their email, raw subject, or canonical id; a bootstrapped admin is unaffected (superset of today's email branch).

### 6.2 Grant/lookup reconciliation (T3)
Confirm the "assign System Admin" path writes `IsSysAdmin` against the same `User.UserId` the key owner resolves to. If the grant endpoint (`SetSysAdminAsync`, matched on `UserId`) can receive a canonical id while the record is keyed on the raw subject (or vice-versa), the grant silently no-ops and the user never actually becomes SysAdmin — a second manifestation of the same root cause. The fix routes the grant lookup through the same canonical resolver (`GetByUserIdOrEmailAsync` + subject normalization) so grant and auth agree. **This is the leading candidate to verify first in §8 V0.**

### 6.3 Cache correctness
`RoleCacheService` keys entries on the identifier passed in and refuses to cache emails (`:44-48`). The fix must key the cache on the **resolved canonical id**, not the raw `CreatedBy`, so two keys for the same owner (different forms) don't produce divergent cached role sets and invalidation (`InvalidateUserRoles`) still lands. Resolve-then-cache, mirroring the email-folding comment already in that file.

## 7. Decisions needed (each has a safe default — reply `@xianix go with defaults` to accept all)

1. **Fix scope** — resolution-only, or also normalize `CreatedBy` at creation **and** backfill existing keys? *Default: resolution-only now (fixes all live keys with zero migration risk); file a follow-up for normalize-on-write + one-shot backfill (D1).*
2. **Canonical identifier direction** — standardize `User.UserId`/`CreatedBy` on canonical `provider|subject` going forward (with migration + an ADR amending ADR-0005), or keep raw-subject storage and make lookups form-agnostic? *Default: keep storage as-is, make lookups form-agnostic — smallest blast radius, no data migration.*
3. **SysAdmin multi-account folding** — keep `EmailIdentityResolution`'s "every record holding the address must agree on SysAdmin" rule for the email path, or treat a key owner's directly-resolved record as authoritative for its own roles? *Default: keep the existing conservative rule; the direct-userId/canonical match already bypasses folding for single-record owners, so Visma admins are unaffected.*

## 8. Verification & test plan

- **V0 (confirm root cause first):** pull one *failing* Visma admin's `User` record + their `ApiKey.CreatedBy` from a repro/staging DB and check which of these holds — (i) `CreatedBy` ≠ `User.UserId` form (canonical vs raw vs legacy email), or (ii) `IsSysAdmin=false` because the grant targeted a different id form (§6.2), or (iii) `IsLockedOut=true` (NeedsReview). The fix covers (i) and (ii); (iii) would redirect the design to the admission/review flow. Decide before coding.
- **Unit — `AdminRoleTenantResolverTests` / `UserRepositorySysAdminRoleTests`:** owner resolves to `SysAdmin` when `CreatedBy` is (a) email, (b) raw subject matching `UserId`, (c) canonical `provider|subject` while `UserId` is the raw subject, (d) raw subject while a legacy key stored canonical; plus negatives: locked-out → no roles, ambiguous email with disagreeing SysAdmin → no SysAdmin (existing `EmailIdentityResolution` behavior preserved).
- **Integration — `AdminApiKeyEndpointsTests` / `AdminAuthEndpointsTests`:** (1) bootstrapped admin key (email) → 200 on a privileged admin route (regression guard); (2) OIDC-provisioned SysAdmin (`UserId` = raw subject, `IsSysAdmin=true`) whose key `CreatedBy` = raw subject → **200** (today's reported 401); (3) same owner, key `CreatedBy` = canonical form → 200; (4) non-admin OIDC user key → still 401.
- **Cache:** two keys for one owner in different forms yield identical resolved roles; `InvalidateUserRoles` after a role change drops both (keyed on resolved canonical id).
- **Conformance:** fitness checks in `docs/architecture/fitness-functions.md` stay green (no new `Shared→Features`, repo-only Mongo, endpoint policy intact).
