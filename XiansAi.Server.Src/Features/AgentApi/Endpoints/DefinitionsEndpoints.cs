using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using Shared.Services;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Features.AgentApi.Endpoints;

/// <summary>
/// Request model for deploying a template agent via AgentAPI.
/// </summary>
public class DeployTemplateAgentRequest
{
    /// <summary>
    /// The name of the system-scoped agent to deploy.
    /// </summary>
    [Required(ErrorMessage = "Agent name is required")]
    [JsonPropertyName("agentName")]
    public required string AgentName { get; set; }
}

// Non-static class for logger type parameter
public class DefinitionsEndpointLogger {}

public static class DefinitionsEndpoints
{
    private static ILogger<DefinitionsEndpointLogger> _logger = null!;

    public static void MapDefinitionsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DefinitionsEndpointLogger>();
        
        // Map definitions endpoints
        var definitionsGroup = app.MapGroup("/api/agent/definitions")
            .WithTags("AgentAPI - Definitions")
            .RequiresCertificate();
            
        definitionsGroup.MapPost("", async (
            [FromBody] FlowDefinitionRequest request,
            [FromServices] IDefinitionsService endpoint) =>
        {
            return await endpoint.CreateAsync(request);
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Create flow definition";
            operation.Description = "Creates a new flow definition in the system";
            return operation;
        });

        definitionsGroup.MapGet("/check", async (
            [FromQuery] string workflowType,
            [FromQuery] bool systemScoped,
            [FromQuery] string hash,
            [FromServices] IDefinitionsService endpoint) =>
        {
            return await endpoint.CheckHash(workflowType, systemScoped, hash);
        })
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation =>
        {
            operation.Summary = "Check if flow definition hash already exists";
            operation.Description = "Checks if a flow definition with the same workflow type and hash already exists to prevent unnecessary uploads.";
            return operation;
        });

        definitionsGroup.MapPost("/agent", async (
            [FromBody] CreateAgentRequest request,
            [FromServices] IDefinitionsService endpoint) =>
        {
            return await endpoint.CreateAgentAsync(request);
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation =>
        {
            operation.Summary = "Create agent";
            operation.Description = "Creates a new agent prior to publishing flow definitions. This is optional - agents can also be created automatically when publishing definitions.";
            return operation;
        });

        definitionsGroup.MapPost("/deploy-template", async (
            [FromBody] DeployTemplateAgentRequest request,
            [FromServices] ITemplateService service) =>
        {
            var result = await service.DeployTemplate(request.AgentName);
            return result.ToHttpResult();
        })
        .RequiresValidTenantAdmin()
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation =>
        {
            operation.Summary = "Deploy a template agent to agent's tenant";
            operation.Description = "Creates a replica of a system-scoped agent, its flow definitions, and associated knowledge in the agent's tenant. Requires tenant admin or system admin role.";
            return operation;
        });

        definitionsGroup.MapDelete("/agent", async (
            [FromQuery] string agentName,
            [FromQuery] bool systemScoped,
            [FromServices] IAgentDeletionService agentService) =>
        {
            var result = await agentService.DeleteAgentAsync(agentName, systemScoped);
            return result.ToHttpResult();
        })
        .RequiresValidTenantAdmin()
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation =>
        {
            operation.Summary = "Delete an agent and associated resources";
            operation.Description = "Deletes the specified agent (template/system or deployed) and cascades removal of definitions, knowledge, schedules, documents, logs, usage events, and related API keys.";
            return operation;
        });
    }
} 