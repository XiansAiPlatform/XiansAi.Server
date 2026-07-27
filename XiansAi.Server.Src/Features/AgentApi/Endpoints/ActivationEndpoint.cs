using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

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
                LogSanitizer.Sanitize(activationName), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(workflowType), LogSanitizer.Sanitize(workflowId), LogSanitizer.Sanitize(tenantId));

            var activation = await activationRepository.GetByNameAndAgentAsync(tenantId, agentName, activationName);

            if (activation == null)
            {
                _logger.LogWarning(
                    "Activation not found: activationName={ActivationName}, agentName={AgentName}, tenantId={TenantId}",
                    LogSanitizer.Sanitize(activationName), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
                return Results.Problem(
                    $"Activation '{activationName}' not found for agent '{agentName}'",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (!activation.IsActive)
            {
                _logger.LogWarning(
                    "Activation '{ActivationName}' for agent '{AgentName}' in tenant '{TenantId}' is not active",
                    LogSanitizer.Sanitize(activationName), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
                return Results.Problem(
                    $"Activation '{activationName}' is not active",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!activation.WorkflowIds.Contains(workflowId))
            {
                _logger.LogWarning(
                    "WorkflowId '{WorkflowId}' is not registered in activation '{ActivationName}'",
                    LogSanitizer.Sanitize(workflowId), LogSanitizer.Sanitize(activationName));
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
                    LogSanitizer.Sanitize(workflowType), LogSanitizer.Sanitize(activationName));
                return Results.Ok(Array.Empty<object>());
            }

            var inputValues = workflowConfig.Inputs
                .Select(input => (object)input.Value)
                .ToArray();

            _logger.LogInformation(
                "Returning {Count} workflow input(s) for workflowType={WorkflowType}, workflowId={WorkflowId}, activationName={ActivationName}",
                inputValues.Length, LogSanitizer.Sanitize(workflowType), LogSanitizer.Sanitize(workflowId), LogSanitizer.Sanitize(activationName));

            return Results.Ok(inputValues);
        })
        .WithName("Get Workflow Input Parameters")
        .Produces<object[]>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("Get workflow input parameters for an activation")
        .WithDescription("Returns the ordered list of input values configured for a specific workflow type ");

        activationGroup.MapGet("/exists", async (
            [FromQuery] string activationName,
            [FromQuery] string agentName,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = ResolveTenantId(tenantContext);
            if (tenantId == null)
            {
                return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
            }

            _logger.LogInformation(
                "Checking activation existence: activationName={ActivationName}, agentName={AgentName}, tenantId={TenantId}",
                LogSanitizer.Sanitize(activationName), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));

            var result = await activationValidationService.ValidateActivationAsync(tenantId, agentName, activationName);
            return result.ToHttpResult();
        })
        .WithName("Check Activation Exists")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("Check whether an activation exists and is active")
        .WithDescription("Returns 200 when the activation exists and is active for the agent in the current tenant; 404 when not found; 409 when deactivated.");

        activationGroup.MapGet("", async (
            [FromQuery] string? agentName,
            [FromServices] IActivationService activationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = ResolveTenantId(tenantContext);
            if (tenantId == null)
            {
                return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
            }

            _logger.LogInformation(
                "Listing activations for tenant {TenantId}, agentName={AgentName}",
                LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(agentName));

            var result = await activationService.GetActivationsByTenantAsync(tenantId, agentName);
            return result.ToHttpResult();
        })
        .WithName("Agent List Activations")
        .Produces<List<AgentActivation>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("List activations for the calling agent's tenant")
        .WithDescription("Lists activations for the certificate tenant, optionally filtered by agentName.");

        activationGroup.MapPost("", async (
            [FromBody] CreateActivationRequest request,
            [FromServices] IActivationService activationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = ResolveTenantId(tenantContext);
            if (tenantId == null)
            {
                return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
            }

            var userId = string.IsNullOrWhiteSpace(tenantContext.LoggedInUser) ? "system" : tenantContext.LoggedInUser;

            _logger.LogInformation(
                "Creating activation {Name} for agent {AgentName} in tenant {TenantId}",
                LogSanitizer.Sanitize(request.Name), LogSanitizer.Sanitize(request.AgentName), LogSanitizer.Sanitize(tenantId));

            var result = await activationService.CreateActivationAsync(request, userId, tenantId);
            return result.ToHttpResult();
        })
        .WithName("Agent Create Activation")
        .Produces<AgentActivation>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("Create an activation")
        .WithDescription("Creates a new activation for an agent in the calling certificate's tenant.");

        activationGroup.MapPost("/{activationId}/activate", async (
            string activationId,
            [FromBody] ActivateAgentRequest? request,
            [FromServices] IActivationService activationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = ResolveTenantId(tenantContext);
            if (tenantId == null)
            {
                return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
            }

            _logger.LogInformation(
                "Activating activation {ActivationId} in tenant {TenantId}",
                LogSanitizer.Sanitize(activationId), LogSanitizer.Sanitize(tenantId));

            var result = await activationService.ActivateAgentAsync(activationId, tenantId, request?.WorkflowConfiguration);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            return Results.Ok(new
            {
                message = $"Agent activation '{activationId}' activated successfully",
                workflowIds = result.Data?.WorkflowIds,
                workflowCount = result.Data?.WorkflowIds?.Count ?? 0,
                activation = result.Data
            });
        })
        .WithName("Agent Activate Activation")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("Activate an activation")
        .WithDescription("Starts workflows for the specified activation in the calling certificate's tenant.");

        activationGroup.MapPost("/{activationId}/deactivate", async (
            string activationId,
            [FromServices] IActivationService activationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = ResolveTenantId(tenantContext);
            if (tenantId == null)
            {
                return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
            }

            _logger.LogInformation(
                "Deactivating activation {ActivationId} in tenant {TenantId}",
                LogSanitizer.Sanitize(activationId), LogSanitizer.Sanitize(tenantId));

            var result = await activationService.DeactivateAgentAsync(activationId, tenantId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }

            return Results.Ok(new
            {
                message = $"Agent activation '{activationId}' deactivated successfully",
                activation = result.Data
            });
        })
        .WithName("Agent Deactivate Activation")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("Deactivate an activation")
        .WithDescription("Cancels workflows and deactivates the specified activation in the calling certificate's tenant.");
    }

    private static string? ResolveTenantId(ITenantContext tenantContext)
    {
        var tenantId = tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("TenantId could not be resolved from certificate context");
            return null;
        }

        return tenantId;
    }
}
