using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Features.AgentApi.Services;
using System.Text.Json;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

namespace Features.AgentApi.Endpoints;

public class WorkflowStarterEndpointLogger {}

public static class WorkflowStarterEndpoints
{
    private static ILogger<WorkflowStarterEndpointLogger> _logger = null!;

    public static void MapWorkflowStarterEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<WorkflowStarterEndpointLogger>();

        _logger.LogInformation("Registering Workflow Starter endpoints...");

        var workflowStarterGroup = app.MapGroup("/api/agent/start-workflow")
            .WithTags("AgentAPI - Workflow Starter")
            .RequiresCertificate();
            // .RequiresValidTenant();
             // Optional: use your desired authorization scheme

        workflowStarterGroup.MapPost("", async (
            [FromServices] IWorkflowStarterService endpoint,
            [FromBody] WorkflowRequest request,
            HttpContext context) =>
        {
            try
            {
                Console.WriteLine("=== Trying to start workflow ===");
                return await endpoint.HandleStartWorkflow(request);
            }
            catch (JsonException)
            {
                return Results.BadRequest("Invalid JSON format");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error starting workflow: {ex.Message}");
            }
        })
        .WithOpenApi(operation =>
        {
            operation.Summary = "Start a new workflow instance";
            operation.Description = "Starts a new workflow instance via the agent endpoint";
            return operation;
        });
    }
}
