using Features.WebApi.Auth;
using Features.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using Features.Shared.Configuration;

namespace Features.WebApi.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this IEndpointRouteBuilder routes)
    {
        var agentGroup = routes.MapGroup("/api/client/agents")
            .WithTags("WebAPI - Agents")
            .RequiresValidTenant()
            .RequireAuthorization()
            .WithGlobalRateLimit(); // Apply standard rate limiting

        agentGroup.MapGet("/names", async (
            [FromQuery] string? scope,
            [FromServices] IAgentService service) =>
        {
            var result = await service.GetAgentNames(scope);
            return result.ToHttpResult();
        })
        .WithName("Get Agent Names")
        
        .WithSummary("Get agent names")
        .WithDescription("Retrieves all agent names");

        agentGroup.MapGet("/all", async (
            [FromServices] IAgentService service) =>
        {
            var result = await service.GetGroupedDefinitions();
            return result.ToHttpResult();
        })
        .WithName("Get Grouped Definitions by Agent")
        
        .WithSummary("Get grouped definitions by agent")
        .WithDescription("Retrieves grouped definitions by agent");
    
        agentGroup.MapGet("/{agentName}/definitions/basic", async (
            string agentName,
            [FromServices] IAgentService service) =>
        {
            var result = await service.GetDefinitions(agentName, true);
            return result.ToHttpResult();
        })
        .WithName("Get Grouped Definitions Basic Info by Agent")
        
        .WithSummary("Get grouped definitions basic info by agent")
        .WithDescription("Retrieves grouped definitions basic info by agent");

        agentGroup.MapGet("/{agentName}/{typeName}/runs", async (
            string agentName,
            string typeName,
            [FromServices] IAgentService service) =>
        {
            var result = await service.GetWorkflowInstances(agentName, typeName);
            return result.ToHttpResult();
        })
        .WithName("Get Run Instances by Agent and Type")
        
        .WithSummary("Get run instances by agent and type")
        .WithDescription("Retrieves run instances by agent and type");

        agentGroup.MapDelete("/{agentName}", async (
            string agentName,
            [FromServices] IAgentService service) =>
        {
            var result = await service.DeleteAgent(agentName);
            return result.ToHttpResult();
        })
        .WithName("Delete Agent")
        
        .WithSummary("Delete agent")
        .WithDescription("Deletes an agent and all its associated flow definitions. Requires owner permission.");
    }
} 