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
            [FromServices] IDefinitionsService endpoint) =>
        {
            return await endpoint.CreateAsync(request);
        })
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
        .WithOpenApi(operation =>
        {
            operation.Summary = "Check if flow definition hash already exists";
            operation.Description = "Checks if a flow definition with the same workflow type and hash already exists to prevent unnecessary uploads.";
            return operation;
        });
    }
} 