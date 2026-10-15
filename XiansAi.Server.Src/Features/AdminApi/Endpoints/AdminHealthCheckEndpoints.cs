using Microsoft.AspNetCore.Mvc;
using Features.AdminApi.Auth;
using Shared.Auth;
using Shared.Configuration;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Services.HealthChecks;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for agent health checks: create and manage checks for an activation,
/// and read their run history and health summary.
/// All routes need the <see cref="CapabilityActions.TenantHealthChecksManage"/> capability.
/// </summary>
public static class AdminHealthCheckEndpoints
{
    public static void MapAdminHealthCheckEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var checkGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/agentActivations/{activationId}/health-checks")
            .WithTags("AdminAPI - Health Checks")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

        checkGroup.MapGet("", async (
            string tenantId,
            string activationId,
            [FromServices] IAgentHealthCheckService service) =>
        {
            var result = await service.ListAsync(tenantId, activationId);
            return result.ToHttpResult();
        })
        .Produces<List<AgentHealthCheck>>()
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminListHealthChecks")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("List an activation's health checks")
        .WithDescription("Lists the health checks attached to the activation, each with its latest status.");

        checkGroup.MapPost("", async (
            string tenantId,
            string activationId,
            [FromBody] HealthCheckRequest request,
            [FromServices] IAgentHealthCheckService service,
            [FromServices] HealthChecksOptions options,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (!options.Enabled)
            {
                return HealthChecksDisabled();
            }

            var result = await service.CreateAsync(tenantId, activationId, request, tenantContext.LoggedInUser ?? "system");
            return result.ToHttpResult();
        })
        .Produces<AgentHealthCheck>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .WithName("AdminCreateHealthCheck")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("Create a health check")
        .WithDescription(
            "Attaches a periodic health check to the activation. type is 'chat', 'webhook' or 'schedule', with the " +
            "matching payload. intervalMinutes must be at least 5. Assertions are case-insensitive 'contains' / " +
            "'notContains' substring checks. A chat check sends a real message to the agent and a webhook check " +
            "invokes the workflow's real update method on every run, so target behavior that is safe to repeat.");

        checkGroup.MapGet("/summary", async (
            string tenantId,
            string activationId,
            [FromServices] IAgentHealthCheckService service) =>
        {
            var result = await service.GetSummaryAsync(tenantId, activationId);
            return result.ToHttpResult();
        })
        .Produces<ActivationHealthSummary>()
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminGetActivationHealthSummary")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("Get an activation's health summary")
        .WithDescription(
            "Returns healthy, degraded, unhealthy or unknown, derived from the latest result of each enabled check " +
            "that has run, plus per-check status.");

        checkGroup.MapGet("/webhook-targets", async (
            string tenantId,
            string activationId,
            [FromServices] IAgentHealthCheckService service) =>
        {
            var result = await service.GetWebhookTargetsAsync(tenantId, activationId);
            return result.ToHttpResult();
        })
        .Produces<List<WebhookTargetWorkflow>>()
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminListHealthCheckWebhookTargets")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("List webhook eval targets for an activation")
        .WithDescription(
            "The agent's workflows and, for each one running under this activation, the Temporal update methods " +
            "a webhook eval can call. Methods are read live from the running workflow, so a workflow that isn't " +
            "running (or whose agent worker is stopped) is listed without methods.");

        checkGroup.MapGet("/{checkId}", async (
            string tenantId,
            string activationId,
            string checkId,
            [FromServices] IAgentHealthCheckService service) =>
        {
            var result = await service.GetAsync(tenantId, activationId, checkId);
            return result.ToHttpResult();
        })
        .Produces<AgentHealthCheck>()
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminGetHealthCheck")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("Get a health check");

        checkGroup.MapPut("/{checkId}", async (
            string tenantId,
            string activationId,
            string checkId,
            [FromBody] HealthCheckRequest request,
            [FromServices] IAgentHealthCheckService service,
            [FromServices] HealthChecksOptions options,
            [FromServices] ITenantContext tenantContext) =>
        {
            if (!options.Enabled)
            {
                return HealthChecksDisabled();
            }

            var result = await service.UpdateAsync(tenantId, activationId, checkId, request, tenantContext.LoggedInUser ?? "system");
            return result.ToHttpResult();
        })
        .Produces<AgentHealthCheck>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .WithName("AdminUpdateHealthCheck")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("Replace a health check")
        .WithDescription("Replaces the check's definition, including enabled. The type cannot be changed.");

        checkGroup.MapDelete("/{checkId}", async (
            string tenantId,
            string activationId,
            string checkId,
            [FromServices] IAgentHealthCheckService service) =>
        {
            var result = await service.DeleteAsync(tenantId, activationId, checkId);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminDeleteHealthCheck")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("Delete a health check")
        .WithDescription("Deletes the check, its schedule and its run history.");

        checkGroup.MapGet("/{checkId}/runs", async (
            string tenantId,
            string activationId,
            string checkId,
            [FromServices] IAgentHealthCheckService service,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            if (!TryParseStatuses(status, out var statuses, out var error) || statuses?.Count > 1)
            {
                return Results.BadRequest(new { error = error ?? "status accepts a single value: pass, fail or error." });
            }

            HealthCheckStatus? statusFilter = statuses is { Count: 1 } ? statuses[0] : null;
            var result = await service.GetRunsAsync(tenantId, activationId, checkId, from, to, statusFilter, page, pageSize);
            return result.ToHttpResult();
        })
        .Produces<HealthCheckRunPage>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("AdminListHealthCheckRuns")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("List a health check's run history")
        .WithDescription(
            "Paged pass/fail/error history, newest first. pageSize is at most 100. History is kept for 30 days " +
            "by default (configurable per deployment).");

        var tenantGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/health-checks")
            .WithTags("AdminAPI - Health Checks")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

        tenantGroup.MapGet("", async (
            string tenantId,
            [FromServices] IAgentHealthCheckService service,
            [FromQuery] string? status = null,
            [FromQuery] string? agentName = null) =>
        {
            if (!TryParseStatuses(status, out var statuses, out var error))
            {
                return Results.BadRequest(new { error });
            }

            var result = await service.ListByTenantAsync(tenantId, statuses, agentName);
            return result.ToHttpResult();
        })
        .Produces<List<AgentHealthCheck>>()
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("AdminListTenantHealthChecks")
        .RequireCapability(CapabilityActions.TenantHealthChecksManage)
        .WithSummary("List health checks across the tenant")
        .WithDescription(
            "Every health check in the tenant with its latest status. Filter with status=fail,error to see what is " +
            "failing right now, and optionally by agentName.");
    }

    private static IResult HealthChecksDisabled() =>
        Results.Problem(
            detail: "Health checks are disabled on this server (HealthChecks:Enabled=false).",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static bool TryParseStatuses(string? value, out List<HealthCheckStatus>? statuses, out string? error)
    {
        statuses = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        statuses = new List<HealthCheckStatus>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<HealthCheckStatus>(part, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                error = $"Unknown status '{part}'. Use pass, fail or error.";
                return false;
            }
            statuses.Add(parsed);
        }

        return true;
    }
}
