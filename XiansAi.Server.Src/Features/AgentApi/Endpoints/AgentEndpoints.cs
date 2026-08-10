using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Repositories;
using Shared.Utils;

namespace Features.AgentApi.Endpoints;

/// <summary>
/// Logger category for agent existence endpoints.
/// </summary>
public class AgentEndpointsLogger { }

/// <summary>
/// AgentApi endpoints for agent discovery under certificate auth.
/// Tenant is resolved from the certificate context.
/// </summary>
public static class AgentEndpoints
{
    /// <summary>
    /// Maps agent discovery endpoints under /api/agent/agents.
    /// </summary>
    public static void MapAgentEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<AgentEndpointsLogger>();

        var agentsGroup = app.MapGroup("/api/agent/agents")
            .WithTags("AgentAPI - Agents")
            .RequiresCertificate();

        agentsGroup.MapGet("/exists", async (
            [FromQuery] string agentName,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                logger.LogWarning("TenantId could not be resolved from certificate context");
                return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.Problem("AgentName is required", statusCode: StatusCodes.Status400BadRequest);
            }

            logger.LogInformation(
                "Checking agent existence: agentName={AgentName}, tenantId={TenantId}",
                LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));

            var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
            // Defense-in-depth: repository scopes by tenant, but never confirm an agent
            // that belongs to a different tenant.
            if (agent == null || !string.Equals(agent.Tenant, tenantId, StringComparison.Ordinal))
            {
                if (agent != null)
                {
                    logger.LogWarning(
                        "Tenant {TenantId} attempted to probe agent '{AgentName}' belonging to tenant {OwnerTenant}",
                        LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(agent.Tenant));
                }
                else
                {
                    logger.LogWarning(
                        "Agent '{AgentName}' not found in tenant {TenantId}",
                        LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
                }

                return Results.Problem(
                    $"Agent '{agentName}' not found",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok();
        })
        .WithName("Check Agent Exists")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("Check whether an agent exists in the calling certificate's tenant")
        .WithDescription("Returns 200 when the agent exists in the tenant; 404 when not found.");
    }
}
