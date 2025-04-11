using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

namespace Features.AgentApi.Endpoints;

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
            [FromServices] DefinitionsService endpoint) =>
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