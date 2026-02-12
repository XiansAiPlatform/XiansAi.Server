using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Features.WebApi.Services;
using Shared.Auth;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

/// <summary>
/// Client API endpoints for listing activation names (e.g. for filter dropdowns).
/// Returns names from the activations collection plus distinct idPostfix values from workflow runs,
/// so the "Choose activations" dropdown lists both DB activations and postfixes that have runs (e.g. from Start Workflow).
/// </summary>
public static class ActivationsEndpoints
{
    public static void MapActivationsEndpoints(this WebApplication app)
    {
        var activationsGroup = app.MapGroup("/api/client/activations")
            .WithTags("WebAPI - Activations")
            .RequiresValidTenant()
            .RequireAuthorization();

        activationsGroup.MapGet("/", async (
            [FromServices] ITenantContext tenantContext,
            [FromServices] IActivationRepository activationRepository,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] IWorkflowFinderService workflowFinderService) =>
        {
            var tenantId = tenantContext.TenantId;
            var userId = tenantContext.LoggedInUser;

            var agents = await agentRepository.GetAgentsWithPermissionAsync(userId, tenantId);
            if (agents == null || agents.Count == 0)
            {
                return Results.Ok(new { names = Array.Empty<string>() });
            }

            var allowedAgentNames = new HashSet<string>(agents.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
            var activations = await activationRepository.GetByTenantIdAsync(tenantId);
            var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in activations.Where(a => allowedAgentNames.Contains(a.AgentName)))
            {
                if (!string.IsNullOrWhiteSpace(a.Name))
                    nameSet.Add(a.Name.Trim());
            }

            // Include distinct idPostfix from Temporal so postfixes used when starting a workflow also appear in the dropdown
            var postfixResult = await workflowFinderService.GetDistinctIdPostfixValuesAsync();
            if (postfixResult.IsSuccess && postfixResult.Data != null)
            {
                foreach (var p in postfixResult.Data)
                {
                    if (!string.IsNullOrWhiteSpace(p))
                        nameSet.Add(p.Trim());
                }
            }

            var names = nameSet.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            return Results.Ok(new { names });
        })
        .WithName("Get Activation Names")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get activation names";
            operation.Description = "Returns activation names for the Runs page dropdown: from the activations collection plus distinct idPostfix values from workflow runs, so all filterable activations appear in Choose activations.";
            return operation;
        });
    }
}
