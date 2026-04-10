using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Repositories;

namespace Features.AgentApi.Endpoints;

public class ActivationEndpointLogger {}

public static class ActivationEndpoints
{
    private static ILogger<ActivationEndpointLogger> _logger = null!;

    public static void MapActivationEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ActivationEndpointLogger>();

        var activationGroup = app.MapGroup("/api/agent/activation")
            .WithTags("AgentAPI - Activation")
            .RequiresCertificate();

        activationGroup.MapGet("/workflow-inputs", async (
            [FromQuery] string activationName,
            [FromQuery] string agentName,
            [FromQuery] string workflowType,
            [FromQuery] string workflowId,
            [FromServices] IActivationRepository activationRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = tenantContext.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("TenantId could not be resolved from certificate context");
                return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
            }

            _logger.LogInformation(
                "Retrieving workflow inputs: activationName={ActivationName}, agentName={AgentName}, workflowType={WorkflowType}, workflowId={WorkflowId}, tenantId={TenantId}",
                activationName, agentName, workflowType, workflowId, tenantId);

            var activation = await activationRepository.GetByNameAndAgentAsync(tenantId, agentName, activationName);

            if (activation == null)
            {
                _logger.LogWarning(
                    "Activation not found: activationName={ActivationName}, agentName={AgentName}, tenantId={TenantId}",
                    activationName, agentName, tenantId);
                return Results.Problem(
                    $"Activation '{activationName}' not found for agent '{agentName}'",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (!activation.IsActive)
            {
                _logger.LogWarning(
                    "Activation '{ActivationName}' for agent '{AgentName}' in tenant '{TenantId}' is not active",
                    activationName, agentName, tenantId);
                return Results.Problem(
                    $"Activation '{activationName}' is not active",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!activation.WorkflowIds.Contains(workflowId))
            {
                _logger.LogWarning(
                    "WorkflowId '{WorkflowId}' is not registered in activation '{ActivationName}'",
                    workflowId, activationName);
                return Results.Problem(
                    $"WorkflowId '{workflowId}' is not registered in activation '{activationName}'",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var workflowConfig = activation.WorkflowConfiguration?.Workflows
                .FirstOrDefault(w => w.WorkflowType == workflowType);

            if (workflowConfig == null)
            {
                _logger.LogWarning(
                    "WorkflowType '{WorkflowType}' not found in activation '{ActivationName}'",
                    workflowType, activationName);
                return Results.Ok(Array.Empty<object>());
            }

            var inputValues = workflowConfig.Inputs
                .Select(input => (object)input.Value)
                .ToArray();

            _logger.LogInformation(
                "Returning {Count} workflow input(s) for workflowType={WorkflowType}, workflowId={WorkflowId}, activationName={ActivationName}",
                inputValues.Length, workflowType, workflowId, activationName);

            return Results.Ok(inputValues);
        })
        .WithName("Get Workflow Input Parameters")
        .Produces<object[]>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Get workflow input parameters for an activation")
        .WithDescription("Returns the ordered list of input values configured for a specific workflow type ");
    }
}
