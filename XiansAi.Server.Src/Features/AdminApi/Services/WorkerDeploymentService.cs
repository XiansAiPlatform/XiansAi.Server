using Google.Protobuf;
using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Features.AdminApi.Services;

/// <summary>A Worker Deployment Version known to Temporal.</summary>
public class WorkerDeploymentVersionSummaryModel
{
    /// <summary>Fully-qualified version identifier, formatted <c>DeploymentName.BuildId</c>.</summary>
    public string Version { get; set; } = string.Empty;
    public string? DrainageStatus { get; set; }
    public DateTime? CreateTime { get; set; }
}

/// <summary>A Worker Deployment and its current routing configuration.</summary>
public class WorkerDeploymentModel
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Temporal namespace the deployment lives in. A tenant whose agents carry an OriginTenant can span
    /// more than one Temporal cluster, so the namespace is what disambiguates two deployments that share
    /// a name.
    /// </summary>
    public string? Namespace { get; set; }

    public DateTime? CreateTime { get; set; }

    /// <summary>
    /// Version currently receiving new executions. <c>__unversioned__</c> means no version has been
    /// promoted, so new workflows still route to unversioned workers even though versioned workers
    /// are registered and polling.
    /// </summary>
    public string? CurrentVersion { get; set; }

    public string? RampingVersion { get; set; }
    public float RampingVersionPercentage { get; set; }
    public string? LastModifierIdentity { get; set; }
    public List<WorkerDeploymentVersionSummaryModel> Versions { get; set; } = new();
}

/// <summary>Result of promoting a version to current or to ramping.</summary>
public class SetWorkerDeploymentVersionResult
{
    public string DeploymentName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? PreviousVersion { get; set; }
    public float? Percentage { get; set; }
}

public interface IWorkerDeploymentService
{
    Task<ServiceResult<List<WorkerDeploymentModel>>> ListDeploymentsAsync(string tenantId);

    Task<ServiceResult<WorkerDeploymentModel>> DescribeDeploymentAsync(string tenantId, string deploymentName);

    Task<ServiceResult<SetWorkerDeploymentVersionResult>> SetCurrentVersionAsync(
        string tenantId, string deploymentName, string buildId, string actor, bool ignoreMissingTaskQueues);

    Task<ServiceResult<SetWorkerDeploymentVersionResult>> SetRampingVersionAsync(
        string tenantId, string deploymentName, string buildId, float percentage, string actor, bool ignoreMissingTaskQueues);
}

/// <summary>
/// Reads and controls Temporal Worker Deployment Versions for a tenant.
/// <para>
/// A worker that opts into Worker Deployment Versioning registers a Worker Deployment Version when it
/// starts, but Temporal does not route new executions to that version until it is promoted to
/// <em>current</em>. On Kubernetes the Temporal Worker Controller performs the promotion; deployments
/// without a controller have no way to do it, which leaves versioned workers polling but idle. These
/// operations close that gap.
/// </para>
/// <para>
/// Worker Deployment is marked experimental by Temporal (as of server 1.28), and the .NET SDK exposes
/// no high-level client for it, so the raw <c>WorkflowService</c> gRPC surface is used directly.
/// </para>
/// </summary>
public class WorkerDeploymentService : IWorkerDeploymentService
{
    /// <summary>Sentinel Temporal reports when no version has been promoted to current.</summary>
    public const string Unversioned = "__unversioned__";

    private const int PageSize = 100;

    private readonly ITemporalGatewayService _temporalGatewayService;
    private readonly ILogger<WorkerDeploymentService> _logger;

    public WorkerDeploymentService(
        ITemporalGatewayService temporalGatewayService,
        ILogger<WorkerDeploymentService> logger)
    {
        _temporalGatewayService = temporalGatewayService ?? throw new ArgumentNullException(nameof(temporalGatewayService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<List<WorkerDeploymentModel>>> ListDeploymentsAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResult<List<WorkerDeploymentModel>>.BadRequest("tenantId is required");
        }

        // A tenant's agents can carry an OriginTenant that routes them to a different Temporal cluster,
        // so listing has to fan out. Querying only the tenant's default client would hide deployments
        // and make an operator conclude a version was never registered.
        var deployments = new List<WorkerDeploymentModel>();
        var clustersQueried = 0;
        Exception? firstFailure = null;

        try
        {
            await foreach (var client in _temporalGatewayService.GetClientsAsync(tenantId))
            {
                try
                {
                    deployments.AddRange(await ListForClientAsync(client));
                    clustersQueried++;
                }
                catch (Exception ex)
                {
                    // One unreachable cluster should not blank out the deployments we can see.
                    firstFailure ??= ex;
                    _logger.LogWarning(ex,
                        "Failed to list worker deployments on namespace {Namespace} for tenant {TenantId}; continuing with remaining clusters",
                        client.Options.Namespace, tenantId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve Temporal clients for tenant {TenantId}", tenantId);
            return ServiceResult<List<WorkerDeploymentModel>>.Failure(
                "Failed to list worker deployments: " + ex.Message, StatusCode.InternalServerError);
        }

        if (clustersQueried == 0 && firstFailure != null)
        {
            return ServiceResult<List<WorkerDeploymentModel>>.Failure(
                "Failed to list worker deployments: " + firstFailure.Message, StatusCode.InternalServerError);
        }

        return ServiceResult<List<WorkerDeploymentModel>>.Success(deployments);
    }

    private static async Task<List<WorkerDeploymentModel>> ListForClientAsync(ITemporalClient client)
    {
        var ns = client.Options.Namespace;
        var deployments = new List<WorkerDeploymentModel>();
        ByteString? pageToken = null;

        do
        {
            var request = new ListWorkerDeploymentsRequest
            {
                Namespace = ns,
                PageSize = PageSize
            };

            if (pageToken != null)
            {
                request.NextPageToken = pageToken;
            }

            var response = await client.WorkflowService.ListWorkerDeploymentsAsync(request);

            foreach (var summary in response.WorkerDeployments)
            {
                deployments.Add(new WorkerDeploymentModel
                {
                    Name = summary.Name,
                    Namespace = ns,
                    CreateTime = ToDateTime(summary.CreateTime),
                    CurrentVersion = ToCanonicalOrUnversioned(summary.RoutingConfig?.CurrentDeploymentVersion),
                    RampingVersion = ToCanonicalVersion(summary.RoutingConfig?.RampingDeploymentVersion),
                    RampingVersionPercentage = summary.RoutingConfig?.RampingVersionPercentage ?? 0
                });
            }

            pageToken = response.NextPageToken.IsEmpty ? null : response.NextPageToken;
        }
        while (pageToken != null);

        return deployments;
    }

    public async Task<ServiceResult<WorkerDeploymentModel>> DescribeDeploymentAsync(string tenantId, string deploymentName)
    {
        var invalid = ValidateNames<WorkerDeploymentModel>(tenantId, deploymentName);
        if (invalid != null)
        {
            return invalid;
        }

        try
        {
            // Xians.Lib defaults a deployment name to the agent name, so passing it as the agent hint
            // routes to that agent's cluster when the tenant spans more than one. An unrecognised name
            // falls back to the tenant's own Temporal config.
            var client = await _temporalGatewayService.GetClientAsync(tenantId, deploymentName);
            var response = await client.WorkflowService.DescribeWorkerDeploymentAsync(
                new DescribeWorkerDeploymentRequest
                {
                    Namespace = client.Options.Namespace,
                    DeploymentName = deploymentName
                });

            var info = response.WorkerDeploymentInfo;

            return ServiceResult<WorkerDeploymentModel>.Success(new WorkerDeploymentModel
            {
                Name = info.Name,
                Namespace = client.Options.Namespace,
                CreateTime = ToDateTime(info.CreateTime),
                CurrentVersion = ToCanonicalOrUnversioned(info.RoutingConfig?.CurrentDeploymentVersion),
                RampingVersion = ToCanonicalVersion(info.RoutingConfig?.RampingDeploymentVersion),
                RampingVersionPercentage = info.RoutingConfig?.RampingVersionPercentage ?? 0,
                LastModifierIdentity = NullIfEmpty(info.LastModifierIdentity),
                Versions = info.VersionSummaries.Select(v => new WorkerDeploymentVersionSummaryModel
                {
                    Version = ToCanonicalVersion(v.DeploymentVersion) ?? string.Empty,
                    DrainageStatus = v.DrainageStatus.ToString(),
                    CreateTime = ToDateTime(v.CreateTime)
                }).ToList()
            });
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            return ServiceResult<WorkerDeploymentModel>.NotFound(
                "Worker deployment '" + deploymentName + "' was not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to describe worker deployment {DeploymentName} for tenant {TenantId}",
                deploymentName, tenantId);
            return ServiceResult<WorkerDeploymentModel>.Failure(
                "Failed to describe worker deployment: " + ex.Message, StatusCode.InternalServerError);
        }
    }

    public async Task<ServiceResult<SetWorkerDeploymentVersionResult>> SetCurrentVersionAsync(
        string tenantId, string deploymentName, string buildId, string actor, bool ignoreMissingTaskQueues)
    {
        var invalid = ValidateSetRequest(tenantId, deploymentName, buildId);
        if (invalid != null)
        {
            return invalid;
        }

        try
        {
            var client = await _temporalGatewayService.GetClientAsync(tenantId, deploymentName);
            var ns = client.Options.Namespace;

            // Read first: the conflict token makes the promotion optimistic-concurrency safe, so two
            // concurrent promotions cannot silently overwrite one another.
            var describe = await client.WorkflowService.DescribeWorkerDeploymentAsync(
                new DescribeWorkerDeploymentRequest { Namespace = ns, DeploymentName = deploymentName });

            var bareBuildId = StripDeploymentPrefix(deploymentName, buildId);

            var response = await client.WorkflowService.SetWorkerDeploymentCurrentVersionAsync(
                new SetWorkerDeploymentCurrentVersionRequest
                {
                    Namespace = ns,
                    DeploymentName = deploymentName,
                    BuildId = bareBuildId,
                    Identity = actor,
                    IgnoreMissingTaskQueues = ignoreMissingTaskQueues,
                    ConflictToken = describe.ConflictToken
                });

            var version = QualifyVersion(deploymentName, bareBuildId);
            var previous = ToCanonicalVersion(response.PreviousDeploymentVersion);

            _logger.LogInformation(
                "Tenant {TenantId}: promoted worker deployment {DeploymentName} current version to {Version} (previous {PreviousVersion}) by {Actor}",
                tenantId, deploymentName, version, previous, actor);

            return ServiceResult<SetWorkerDeploymentVersionResult>.Success(new SetWorkerDeploymentVersionResult
            {
                DeploymentName = deploymentName,
                Version = version,
                PreviousVersion = previous
            });
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            return ServiceResult<SetWorkerDeploymentVersionResult>.NotFound(
                "Worker deployment '" + deploymentName + "' or build '" + buildId + "' was not found");
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.FailedPrecondition)
        {
            // Temporal raises this when the target version does not poll every task queue the current
            // version serves. Surfaced as 409 so the caller can decide whether to retry with
            // ignoreMissingTaskQueues rather than have the server silently override the protection.
            return ServiceResult<SetWorkerDeploymentVersionResult>.Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set current version for worker deployment {DeploymentName}, tenant {TenantId}",
                deploymentName, tenantId);
            return ServiceResult<SetWorkerDeploymentVersionResult>.Failure(
                "Failed to set current version: " + ex.Message, StatusCode.InternalServerError);
        }
    }

    public async Task<ServiceResult<SetWorkerDeploymentVersionResult>> SetRampingVersionAsync(
        string tenantId, string deploymentName, string buildId, float percentage, string actor, bool ignoreMissingTaskQueues)
    {
        var invalid = ValidateSetRequest(tenantId, deploymentName, buildId);
        if (invalid != null)
        {
            return invalid;
        }

        if (percentage < 0 || percentage > 100)
        {
            return ServiceResult<SetWorkerDeploymentVersionResult>.BadRequest("percentage must be between 0 and 100");
        }

        try
        {
            var client = await _temporalGatewayService.GetClientAsync(tenantId, deploymentName);
            var ns = client.Options.Namespace;

            var describe = await client.WorkflowService.DescribeWorkerDeploymentAsync(
                new DescribeWorkerDeploymentRequest { Namespace = ns, DeploymentName = deploymentName });

            var bareBuildId = StripDeploymentPrefix(deploymentName, buildId);
            var version = QualifyVersion(deploymentName, bareBuildId);

            await client.WorkflowService.SetWorkerDeploymentRampingVersionAsync(
                new SetWorkerDeploymentRampingVersionRequest
                {
                    Namespace = ns,
                    DeploymentName = deploymentName,
                    BuildId = bareBuildId,
                    Percentage = percentage,
                    Identity = actor,
                    IgnoreMissingTaskQueues = ignoreMissingTaskQueues,
                    ConflictToken = describe.ConflictToken
                });

            _logger.LogInformation(
                "Tenant {TenantId}: set worker deployment {DeploymentName} ramping version to {Version} at {Percentage} percent by {Actor}",
                tenantId, deploymentName, version, percentage, actor);

            return ServiceResult<SetWorkerDeploymentVersionResult>.Success(new SetWorkerDeploymentVersionResult
            {
                DeploymentName = deploymentName,
                Version = version,
                Percentage = percentage
            });
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            return ServiceResult<SetWorkerDeploymentVersionResult>.NotFound(
                "Worker deployment '" + deploymentName + "' or build '" + buildId + "' was not found");
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.FailedPrecondition)
        {
            return ServiceResult<SetWorkerDeploymentVersionResult>.Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set ramping version for worker deployment {DeploymentName}, tenant {TenantId}",
                deploymentName, tenantId);
            return ServiceResult<SetWorkerDeploymentVersionResult>.Failure(
                "Failed to set ramping version: " + ex.Message, StatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Builds the canonical version identifier (<c>DeploymentName.BuildId</c>) used in responses and logs,
    /// tolerating a buildId that already carries the deployment prefix.
    /// </summary>
    public static string QualifyVersion(string deploymentName, string buildId) =>
        buildId.StartsWith(deploymentName + ".", StringComparison.Ordinal)
            ? buildId
            : deploymentName + "." + buildId;

    /// <summary>
    /// Reduces a possibly fully-qualified version to the bare build ID. The wire API takes deployment
    /// name and build ID as separate fields, but callers naturally paste the canonical
    /// <c>DeploymentName.BuildId</c> string they see in the CLI and UI, so both are accepted.
    /// </summary>
    public static string StripDeploymentPrefix(string deploymentName, string buildId)
    {
        var prefix = deploymentName + ".";
        return buildId.StartsWith(prefix, StringComparison.Ordinal)
            ? buildId.Substring(prefix.Length)
            : buildId;
    }

    /// <summary>Formats a structured deployment version as <c>DeploymentName.BuildId</c>, or null when unset.</summary>
    private static string? ToCanonicalVersion(Temporalio.Api.Deployment.V1.WorkerDeploymentVersion? version) =>
        version == null || string.IsNullOrEmpty(version.BuildId)
            ? null
            : version.DeploymentName + "." + version.BuildId;

    /// <summary>
    /// Same as <see cref="ToCanonicalVersion"/> but reports <see cref="Unversioned"/> rather than null,
    /// matching what Temporal's CLI shows for a deployment with no promoted version.
    /// </summary>
    private static string ToCanonicalOrUnversioned(Temporalio.Api.Deployment.V1.WorkerDeploymentVersion? version) =>
        ToCanonicalVersion(version) ?? Unversioned;

    private static ServiceResult<T>? ValidateNames<T>(string tenantId, string deploymentName)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResult<T>.BadRequest("tenantId is required");
        }

        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            return ServiceResult<T>.BadRequest("deploymentName is required");
        }

        return null;
    }

    private static ServiceResult<SetWorkerDeploymentVersionResult>? ValidateSetRequest(
        string tenantId, string deploymentName, string buildId)
    {
        var invalid = ValidateNames<SetWorkerDeploymentVersionResult>(tenantId, deploymentName);
        if (invalid != null)
        {
            return invalid;
        }

        if (string.IsNullOrWhiteSpace(buildId))
        {
            return ServiceResult<SetWorkerDeploymentVersionResult>.BadRequest("buildId is required");
        }

        return null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static DateTime? ToDateTime(Google.Protobuf.WellKnownTypes.Timestamp? timestamp) =>
        timestamp?.ToDateTime();
}
