using Features.WebApi.Auth;
using Features.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace XiansAi.Server.Features.WebApi.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this IEndpointRouteBuilder routes)
    {
        var agentGroup = routes.MapGroup("/api/client/agents")
            .WithTags("WebAPI - Agents")
            .RequiresValidTenant();

        
        agentGroup.MapGet("/all", async (
            [FromServices] IAgentService service) =>
        {
            return await service.GetAgentNames();
        })
        .WithName("Get All Agents")
        .WithOpenApi(operation => {
            operation.Summary = "Get all agents";
            operation.Description = "Retrieves all agents";
            return operation;
        });

        agentGroup.MapGet("/workflows/all", async (
            [FromServices] IAgentService service) =>
        {
            return await service.GetGroupedDefinitions();
        })
        .WithName("Get Grouped Definitions")
        .WithOpenApi(operation => {
            operation.Summary = "Get grouped definitions";
            operation.Description = "Retrieves grouped definitions";
            return operation;
        });

        agentGroup.MapGet("/workflows", async (
            [FromQuery] string? agentName,
            [FromQuery] string? typeName,
            [FromServices] IAgentService service) =>
        {
            return await service.GetWorkflowInstances(agentName, typeName);
        })
        .WithName("Get Workflow Instances")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflow instances";
            operation.Description = "Retrieves workflow instances";
            return operation;
        });
    }
} 