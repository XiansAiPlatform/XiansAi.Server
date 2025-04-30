using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Shared.Utils.Services;
using Features.WebApi.Services;

namespace Features.WebApi.Endpoints;

public static class AuditingEndpoints
{
    public static void MapAuditingEndpoints(this WebApplication app)
    {
        // Map auditing endpoints with common attributes
        var auditingGroup = app.MapGroup("/api/client/auditing")
            .WithTags("WebAPI - Auditing")
            .RequiresValidTenant();

        auditingGroup.MapGet("/agents", async (
            [FromServices] IAuditingService auditingService) =>
        {
            var result = await auditingService.GetAgentDefinitions();
            return result.ToHttpResult();
        })
        .WithName("Get Agent Definitions")
        .WithOpenApi(operation => {
            operation.Summary = "Get agent definitions";
            operation.Description = "Retrieves all agent definitions";
            return operation;
        });

        auditingGroup.MapGet("/workflows", async (
            [FromQuery] string? agentName,
            [FromQuery] string? typeName,
            [FromServices] IAuditingService auditingService) =>
        {
            Console.WriteLine("agentName called: " + agentName);
            var result = await auditingService.GetWorkflows(agentName, typeName);
            return result.ToHttpResult();
        })
        .WithName("Get Audited Workflows")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflow audit data";
            operation.Description = "Retrieves audit data for workflows with optional filtering";
            return operation;
        });
    }
} 