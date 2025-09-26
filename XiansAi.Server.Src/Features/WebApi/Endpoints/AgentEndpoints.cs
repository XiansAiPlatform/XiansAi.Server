using Features.WebApi.Auth;
using Features.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this IEndpointRouteBuilder routes)
    {
        var agentGroup = routes.MapGroup("/api/client/agents")
            .WithTags("WebAPI - Agents")
            .RequiresValidTenant()
            .RequireAuthorization();

        agentGroup.MapGet("/names", async (
            [FromServices] IAgentService service) =>
        {
            var result = await service.GetAgentNames();
            return result.ToHttpResult();
        })
        .WithName("Get Agent Names")
        .WithOpenApi(operation => {
            operation.Summary = "Get agent names";
            operation.Description = "Retrieves all agent names";
            return operation;
        });

        agentGroup.MapGet("/all", async (
            [FromServices] IAgentService service) =>
        {
            var result = await service.GetGroupedDefinitions();
            return result.ToHttpResult();
        })
        .WithName("Get Grouped Definitions by Agent")
        .WithOpenApi(operation => {
            operation.Summary = "Get grouped definitions by agent";
            operation.Description = "Retrieves grouped definitions by agent";
            return operation;
        });
    
        agentGroup.MapGet("/{agentName}/definitions/basic", async (
            string agentName,
            [FromServices] IAgentService service) =>
        {
            var result = await service.GetDefinitions(agentName, true);
            return result.ToHttpResult();
        })
        .WithName("Get Grouped Definitions Basic Info by Agent")
        .WithOpenApi(operation => {
            operation.Summary = "Get grouped definitions basic info by agent";
            operation.Description = "Retrieves grouped definitions basic info by agent";
            return operation;
        });

        agentGroup.MapGet("/{agentName}/{typeName}/runs", async (
            string agentName,
            string typeName,
            [FromServices] IAgentService service) =>
        {
            var result = await service.GetWorkflowInstances(agentName, typeName);
            return result.ToHttpResult();
        })
        .WithName("Get Run Instances by Agent and Type")
        .WithOpenApi(operation => {
            operation.Summary = "Get run instances by agent and type";
            operation.Description = "Retrieves run instances by agent and type";
            return operation;
        });

        agentGroup.MapDelete("/{agentName}", async (
            string agentName,
            [FromServices] IAgentService service) =>
        {
            var result = await service.DeleteAgent(agentName);
            return result.ToHttpResult();
        })
        .WithName("Delete Agent")
        .WithOpenApi(operation => {
            operation.Summary = "Delete agent";
            operation.Description = "Deletes an agent and all its associated flow definitions. Requires owner permission.";
            return operation;
        });
    }
} 