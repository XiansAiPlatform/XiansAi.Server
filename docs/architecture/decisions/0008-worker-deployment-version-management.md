# ADR-0008: Worker Deployment Version Management

**Status:** Proposed

**Date:** 2026-09-02

## Context

Xians.Lib supports opt-in Temporal Worker Deployment Versioning. A worker configured with
`XiansOptions.WorkerVersioning` registers a Worker Deployment Version with Temporal when it starts,
which lets a new build roll out without breaking workflows already in flight on the old build
(`Pinned` executions complete on the version that started them).

Registration alone is not sufficient. Temporal keeps routing new executions to unversioned workers
until a version is explicitly promoted to *current*. A deployment that enables versioning and stops
there ends up with versioned workers polling but receiving no work — a silent failure, because every
component reports healthy.

Observed on Temporal 1.28 immediately after a versioned worker connects:

```
Name      CurrentVersion    RampingVersion
wv-smoke  __unversioned__   <none>
```

On Kubernetes the Temporal Worker Controller performs the promotion as part of a rollout. Deployments
without that controller — self-hosted, community edition, local development, and any operator not
running the K8s operator — have no way to promote a version, because the server exposes no such
operation and the Temporal CLI is not always reachable from where the platform is administered.

The server already brokers Temporal access on the tenant's behalf (`ITemporalGatewayService`,
including per-tenant connection overrides), so it is the natural place for this.

## Decision

We add **Worker Deployment Version management** to the AdminApi, under
`/api/v{version}/admin/tenants/{tenantId}/worker-deployments`:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `` | List the tenant's Worker Deployments and routing configuration |
| `GET` | `/{deploymentName}` | Describe one deployment, including known versions and drainage status |
| `POST` | `/{deploymentName}/set-current-version` | Promote a build to current |
| `POST` | `/{deploymentName}/set-ramping-version` | Route a percentage of new executions to a build |

Supporting decisions:

1. **SysAdmin only.** Promotion determines which build of an agent runs every new workflow for a
   tenant, and build IDs originate in the release pipeline rather than in tenant configuration. This
   is platform-operator surface, not tenant self-service. It matches the SysAdmin gate already applied
   to the per-tenant Temporal connection override (`/tenants/{tenantId}/temporal-config`).

2. **No Temporalio SDK upgrade.** The server stays on Temporalio 1.7.0. That version already carries
   the full worker-deployment gRPC surface (`ListWorkerDeployments`, `DescribeWorkerDeployment`,
   `SetWorkerDeploymentCurrentVersion`, `SetWorkerDeploymentRampingVersion`). Neither 1.7.0 nor current
   releases expose a high-level client for it, so the raw `WorkflowService` surface is used directly in
   both cases; upgrading would buy nothing here.

3. **Read-then-write with a conflict token.** Every promotion first calls `DescribeWorkerDeployment`
   and passes the returned `ConflictToken`, so two concurrent promotions cannot silently overwrite one
   another.

4. **`IgnoreMissingTaskQueues` is opt-in per request.** Temporal refuses a promotion when the target
   version does not poll every task queue the current version serves. That refusal surfaces as `409
   Conflict` rather than being suppressed, so an operator overrides the protection deliberately.

5. **Canonical version strings at the boundary, structured fields on the wire.** Responses use the
   `DeploymentName.BuildId` form operators see in the Temporal CLI and UI, and requests accept either
   that form or a bare build ID. Internally the non-deprecated structured `WorkerDeploymentVersion`
   fields are used, not the deprecated flat strings.

6. **A deployment with no promoted version reports `__unversioned__`,** matching the Temporal CLI,
   rather than null or an empty string. This makes the "registered but not receiving work" state
   legible in the API response instead of looking like missing data.

7. **Listing fans out across the tenant's Temporal clusters.** A tenant's agents can carry an
   `OriginTenant` that routes them to a different cluster, so `GET` iterates `GetClientsAsync` rather
   than querying a single client. Listing one cluster would hide deployments and lead an operator to
   conclude a version was never registered. Each result carries its `namespace`, which is what
   distinguishes two deployments that share a name. A cluster that fails to answer is logged and
   skipped rather than failing the whole listing; the request only fails when no cluster answered.

8. **Single-deployment operations use the deployment name as the agent hint.** Xians.Lib defaults a
   deployment name to the agent name, so `GetClientAsync(tenantId, deploymentName)` reaches that agent's
   cluster. A name that matches no agent falls back to the tenant's own Temporal config, so a
   deployment named independently of any agent still resolves.

## Consequences

**Positive:**
- Worker versioning becomes usable without the Kubernetes Worker Controller, which is what makes the
  Xians.Lib feature safe to enable outside SuperOffice's own deployment topology.
- The silent "registered but idle" failure becomes visible: `GET` on the deployment shows
  `__unversioned__` directly.
- Progressive rollout (ramping) is available to operators who want to canary a build.
- No new persistence: Temporal remains the source of truth for deployment state, so there is nothing
  to keep in sync or migrate.

**Negative:**
- Temporal marks Worker Deployment **experimental** as of server 1.28; the wire API may change.
- The endpoints depend on the raw gRPC surface, which is less stable than the SDK's typed client APIs
  would be.
- A mistaken promotion has a wide blast radius: every new workflow for the tenant moves to that build.

**Mitigations:**
- SysAdmin gate plus the conflict token keeps the operation deliberate and race-free.
- `IgnoreMissingTaskQueues` defaults to false, so Temporal's own protection applies unless overridden.
- The service isolates every Temporal type behind its own response models, so an API change is
  contained to `WorkerDeploymentService` rather than reaching endpoints or clients.
- Promotions are logged at information level with tenant, deployment, target version, previous version,
  and the acting user.

## Related

- ADR-0001 (feature-slice architecture) — the feature lives in `Features/AdminApi/`.
- ADR-0002 (minimal API pattern) — endpoints are minimal-API definitions delegating to a service.
- Xians.Lib `XiansOptions.WorkerVersioning` — the worker-side half of this feature.
