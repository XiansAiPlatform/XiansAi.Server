using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Shared.Services;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

namespace Features.WebApi.Endpoints;

/// <summary>
/// Request model for deploying a template agent.
/// </summary>
public class DeployTemplateRequest
{
    /// <summary>
    /// The name of the system-scoped agent to deploy.
    /// </summary>
    [Required(ErrorMessage = "Agent name is required")]
    public required string AgentName { get; set; }
}

public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this WebApplication app)
    {
        // Map template endpoints with common attributes
        var templateGroup = app.MapGroup("/api/client/templates")
            .WithTags("WebAPI - Templates")
            .RequiresValidTenant()
            .RequireAuthorization();

        templateGroup.MapGet("/agents", async (
            [FromQuery] bool? basicDataOnly,
            [FromServices] ITemplateService service) =>
        {
            var result = await service.GetSystemScopedAgentDefinitions(basicDataOnly ?? false);
            return result.ToHttpResult();
        })
        .WithName("Get System-Scoped Agent Definitions")
        .WithOpenApi(operation => {
            operation.Summary = "Get system-scoped agent definitions";
            operation.Description = "Retrieves all agent definitions that have system_scoped attribute set to true";
            return operation;
        });

        templateGroup.MapPost("/deploy", async (
            [FromBody] DeployTemplateRequest request,
            [FromServices] ITemplateService service) =>
        {
            var result = await service.DeployTemplate(request.AgentName);
            return result.ToHttpResult();
        })
        .WithName("Deploy Template Agent")
        .WithOpenApi(operation => {
            operation.Summary = "Deploy a template agent to user's tenant";
            operation.Description = "Creates a replica of a system-scoped agent and its flow definitions in the user's tenant";
            return operation;
        });

        // System Admin only endpoint for deleting system-scoped agents
        templateGroup.MapDelete("/{agentName}", async (
            [FromRoute] string agentName,
            [FromServices] ITemplateService service) =>
        {
            var result = await service.DeleteSystemScopedAgent(agentName);
            return result.ToHttpResult();
        })
        .RequiresValidSysAdmin()
        .WithName("Delete System-Scoped Agent")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a system-scoped agent";
            operation.Description = "Deletes a system-scoped agent and all its associated flow definitions. This operation is only available to system administrators.";
            return operation;
        });
    }
}
