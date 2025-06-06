using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

//Boilerplate code for future versions

namespace Features.AgentApi.Endpoints.V2;

public class DefinitionsEndpointLogger { }

public static class DefinitionsEndpointsV2
{
    private static ILogger<DefinitionsEndpointLogger> _logger = null!;

    public static void MapDefinitionsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v2";
        _logger = loggerFactory.CreateLogger<DefinitionsEndpointLogger>();

        // Map definitions endpoints
        var definitionsGroup = app.MapGroup($"/api/{version}/agent/definitions")
            .WithTags($"AgentAPI - Definitions {version}")
            .RequiresCertificate();

        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MapRoutes(definitionsGroup, version, registeredPaths);
        
        // Reuse v1 mappings
        V1.DefinitionsEndpointsV1.MapRoutes(definitionsGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        // string RouteKey(string method, string path) => $"{method}:{path}";

        // If v2 has the same endpoint with changes, we can overwrite it, before v1 is called this method will be called and hashset will record that it is already called
        // Hence v1 would not register the same endpoint again

        // var routePath = "/";
        // if (registeredPaths.Add(RouteKey("POST", routePath)))
        // {
        //     group.MapPost("", async (
        //         [FromBody] FlowDefinitionRequest request,
        //         [FromServices] IDefinitionsService endpoint) =>
        //     {
        //         return await endpoint.CreateAsync(request);
        //     })
        //     .WithOpenApi(operation => {
        //         operation.Summary = "Create flow definition";
        //         operation.Description = "Creates a new flow definition in the system";
        //         return operation;
        //     }); 
        // }
    }
} 