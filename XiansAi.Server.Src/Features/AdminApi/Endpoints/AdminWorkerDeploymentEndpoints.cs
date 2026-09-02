using Microsoft.AspNetCore.Mvc;
using Features.AdminApi.Auth;
using Features.AdminApi.Services;
using Shared.Auth;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>Request body for promoting a build to the current version.</summary>
public class SetCurrentVersionRequest
{
    /// <summary>
    /// Build ID to promote. May be given bare (<c>1.4.0</c>) or fully qualified
    /// (<c>my-deployment.1.4.0</c>); the deployment name is taken from the route either way.
    /// </summary>
    public required string BuildId { get; set; }

    /// <summary>
    /// Promote even when the target version does not poll every task queue the current version serves.
    /// Defaults to false, which keeps Temporal's protection against stranding a task queue.
    /// </summary>
    public bool IgnoreMissingTaskQueues { get; set; } = false;
}

/// <summary>Request body for directing a percentage of new executions to a build.</summary>
public class SetRampingVersionRequest
{
    /// <summary>Build ID to ramp traffic to. Bare or fully qualified.</summary>
    public required string BuildId { get; set; }

    /// <summary>Percentage of new executions to route to this version, 0-100.</summary>
    public float Percentage { get; set; }

    /// <summary>See <see cref="SetCurrentVersionRequest.IgnoreMissingTaskQueues"/>.</summary>
    public bool IgnoreMissingTaskQueues { get; set; } = false;
}

/// <summary>
/// AdminApi endpoints for inspecting and controlling Temporal Worker Deployment Versions.
/// All endpoints are under
/// <c>/api/v{version}/admin/tenants/{tenantId}/worker-deployments</c>.
/// <para>
/// SysAdmin only. Promoting a version decides which build of an agent runs every new workflow for the
/// tenant, and build IDs originate in the release pipeline rather than in tenant configuration, so this
/// is platform-operator surface rather than tenant self-service. This mirrors the SysAdmin gate already
/// applied to the per-tenant Temporal connection override.
/// </para>
/// </summary>
public static class AdminWorkerDeploymentEndpoints
{
    /// <summary>Maps all AdminApi worker deployment endpoints.</summary>
    public static void MapAdminWorkerDeploymentEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var deploymentGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/worker-deployments")
            .WithTags("AdminAPI - Worker Deployments")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<SysAdminOnlyFilter>()
            .AddEndpointFilter<TenantRouteScopeFilter>();

        deploymentGroup.MapGet("", async (
            string tenantId,
            [FromServices] IWorkerDeploymentService service) =>
        {
            var result = await service.ListDeploymentsAsync(tenantId);
            return result.ToHttpResult();
        })
        .Produces<List<WorkerDeploymentModel>>()
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("AdminListWorkerDeployments")
        .WithSummary("List Worker Deployments")
        .WithDescription(
            "Lists the tenant's Temporal Worker Deployments and their routing configuration. " +
            "A currentVersion of __unversioned__ means no version has been promoted, so new workflows " +
            "still route to unversioned workers even when versioned workers are registered and polling.");

        deploymentGroup.MapGet("/{deploymentName}", async (
            string tenantId,
            string deploymentName,
            [FromServices] IWorkerDeploymentService service) =>
        {
            var result = await service.DescribeDeploymentAsync(tenantId, deploymentName);
            return result.ToHttpResult();
        })
        .Produces<WorkerDeploymentModel>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithName("AdminDescribeWorkerDeployment")
        .WithSummary("Describe a Worker Deployment")
        .WithDescription("Returns routing configuration and the known versions of a single Worker Deployment, including each version's drainage status.");

        deploymentGroup.MapPost("/{deploymentName}/set-current-version", async (
            string tenantId,
            string deploymentName,
            [FromBody] SetCurrentVersionRequest request,
            [FromServices] IWorkerDeploymentService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var actor = tenantContext.LoggedInUser ?? "system";
            var result = await service.SetCurrentVersionAsync(
                tenantId, deploymentName, request.BuildId, actor, request.IgnoreMissingTaskQueues);
            return result.ToHttpResult();
        })
        .Produces<SetWorkerDeploymentVersionResult>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithName("AdminSetWorkerDeploymentCurrentVersion")
        .WithSummary("Promote a build to the current version")
        .WithDescription(
            "Routes all new executions for this deployment to the given build. Required before a versioned " +
            "worker receives any work: registering a version does not by itself make it current. Returns 409 " +
            "when the target version does not poll every task queue the current version serves; retry with " +
            "ignoreMissingTaskQueues to override.");

        deploymentGroup.MapPost("/{deploymentName}/set-ramping-version", async (
            string tenantId,
            string deploymentName,
            [FromBody] SetRampingVersionRequest request,
            [FromServices] IWorkerDeploymentService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var actor = tenantContext.LoggedInUser ?? "system";
            var result = await service.SetRampingVersionAsync(
                tenantId, deploymentName, request.BuildId, request.Percentage, actor, request.IgnoreMissingTaskQueues);
            return result.ToHttpResult();
        })
        .Produces<SetWorkerDeploymentVersionResult>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithName("AdminSetWorkerDeploymentRampingVersion")
        .WithSummary("Ramp a percentage of traffic to a build")
        .WithDescription(
            "Routes the given percentage (0-100) of new executions to a build while the rest continue on the " +
            "current version, for progressive rollout ahead of a full promotion.");
    }
}
