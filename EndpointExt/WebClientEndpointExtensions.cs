using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.EndpointExt.WebClient;

namespace XiansAi.Server.EndpointExt;
public static class WebClientEndpointExtensions
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        MapWorkflowEndpoints(app);
        MapInstructionEndpoints(app);
        MapActivityEndpoints(app);
        MapSettingsEndpoints(app);
        MapDefinitionsEndpoints(app);
    }

    private static void MapDefinitionsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/definitions", async (
            [FromServices] DefinitionsEndpoint endpoint) =>
        {
            return await endpoint.GetLatestDefinitions();
        })
        .WithName("Get Latest Definitions")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();
    }

    private static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/client/certificates/generate", async (
            HttpContext context,
            [FromBody] CertRequest request,
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return await endpoint.GenerateClientCertificate(request);
        })
        .WithName("Generate Client Certificate")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Generate a new client certificate";
            operation.Description = "Generates and returns a new client certificate in PFX format";
            return operation;
        });

        app.MapGet("/api/client/settings/flowserver", (
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return endpoint.GetFlowServerSettings();
        })
        .WithName("Get Flow Server Settings")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi(); 

        app.MapGet("/api/client/certificates/flowserver/cert", async (
            [FromQuery] string fileName,
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return await endpoint.GetFlowServerCertFile(fileName);
        })
        .WithName("Get Flow Server Certificate File")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi(); 

        app.MapGet("/api/client/certificates/flowserver/privatekey", async (
            [FromQuery] string fileName,
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return await endpoint.GetFlowServerPrivateKeyFile(fileName);
        })
        .WithName("Get Flow Server Private Key File")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi(); 
    }

    private static void MapActivityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/workflows/{workflowId}/activities/{activityId}", async (
            string workflowId,
            string activityId,
            [FromServices] ActivitiesEndpoint endpoint) =>
        {
            return await endpoint.GetActivity(workflowId, activityId);
        }).WithName("Get Activity of Workflow")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();
    }

    private static void MapInstructionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/instructions/{id}", async (
            string id,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetInstruction(id);
        })
        .WithName("Get Instruction")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions", async (
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetInstructions();
        })
        .WithName("Get Instructions")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapPost("/api/client/instructions", async (
            [FromBody] InstructionRequest request,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.CreateInstruction(request);
        })
        .WithName("Create Instruction")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions/latest", async (
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetLatestInstructions();
        })
        .WithName("Get Latest Instructions")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapDelete("/api/client/instructions/{id}", async (
            string id,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.DeleteInstruction(id);
        })
        .WithName("Delete Instruction")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapDelete("/api/client/instructions/all", async (
            [FromBody] DeleteAllVersionsRequest request,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.DeleteAllVersions(request);
        })
        .WithName("Delete All Versions")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions/versions", async (
            [FromQuery]string name,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetInstructionVersions(name);
        })
        .WithName("Get Instruction Versions")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();
    }
    
    private static void MapWorkflowEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/workflows/{workflowId}", async (
            string workflowId,
            [FromServices] WorkflowFinderEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflow(workflowId);
        })
        .WithName("Get Workflow")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapPost("/api/client/workflows", async (
            [FromBody] WorkflowRequest request,
            [FromServices] WorkflowStarterEndpoint endpoint) =>
        {
            return await endpoint.HandleStartWorkflow(request);
        })
        .WithName("Create New Workflow")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapGet("/api/client/workflows/{workflowId}/events", async (
            string workflowId,
            [FromServices] WorkflowEventsEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflowEvents(workflowId);
        })
        .WithName("Get Workflow Events")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapGet("/api/client/workflows", async (
            [FromServices] WorkflowFinderEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflows();
        })
        .WithName("Get Workflows")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapPost("/api/client/workflows/{workflowId}/cancel", async (
            string workflowId,
            [FromQuery] bool force,
            [FromServices] WorkflowCancelEndpoint endpoint) =>
        {
            return await endpoint.CancelWorkflow(workflowId, force);
        })
        .WithName("Cancel Workflow")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi();

        app.MapGet("/api/client/workflows/{workflowId}/events/stream", (
            [FromServices] WorkflowEventsEndpoint endpoint,
            string workflowId) =>
        {
            return endpoint.StreamWorkflowEvents(workflowId);
        })
        .WithName("Stream Workflow Events")
        .RequireAuthorization("RequireJwtAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Stream workflow events in real-time";
            operation.Description = "Provides a server-sent events (SSE) stream of workflow activity events";
            return operation;
        });
    }
}