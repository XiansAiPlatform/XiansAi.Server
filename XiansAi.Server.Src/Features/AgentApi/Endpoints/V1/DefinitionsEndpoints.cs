using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

namespace Features.AgentApi.Endpoints.V1;

// Non-static class for logger type parameter
public class DefinitionsEndpointLogger {}

public static class DefinitionsEndpointsV1
{
    private static ILogger<DefinitionsEndpointLogger> _logger = null!;

    public static void MapDefinitionsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v1";
        _logger = loggerFactory.CreateLogger<DefinitionsEndpointLogger>();
        
        // Map definitions endpoints
        var definitionsGroup = app.MapGroup($"/api/{version}/agent/definitions")
            .WithTags($"AgentAPI - Definitions {version}")
            .RequiresCertificate();
        
        // If there are any routes that are common for multiple versions, add them here
        CommonMapRoutes(definitionsGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(definitionsGroup, version);
    }

    internal static void CommonMapRoutes(RouteGroupBuilder group, string version)
    {
        // If there are any routes that are common for multiple versions, add them here
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        group.MapPost("", async (
            [FromBody] FlowDefinitionRequest request,
            [FromServices] IDefinitionsService endpoint) =>
        {
            return await endpoint.CreateAsync(request);
        })
        .WithOpenApi(operation => {
            operation.Summary = "Create flow definition";
            operation.Description = "Creates a new flow definition in the system";
            return operation;
        });
    }
} 