using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.Services.Web;

namespace XiansAi.Server.Endpoints;
public static class WebEndpointExtensions
{
    public static void MapWebEndpoints(this WebApplication app)
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
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] string? owner,
            [FromServices] DefinitionsEndpoint endpoint) =>
        {
            return await endpoint.GetLatestDefinitions(startTime, endTime, owner);
        })
        .WithName("Get Latest Definitions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get definitions with optional filters";
            operation.Description = "Retrieves definitions with optional filtering by date range and owner";
            return operation;
        });
    }

    private static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/client/certificates/generate/base64", async (
            HttpContext context,
            [FromBody] CertRequest request,
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return await endpoint.GenerateClientCertificateBase64(request);
        })
        .WithName("Generate Client Certificate Base64")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Generate a new client certificate in base64 format";
            operation.Description = "Generates and returns a new client certificate in base64 format";
            return operation;
        });

        app.MapPost("/api/client/certificates/generate", async (
            HttpContext context,
            [FromBody] CertRequest request,
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return await endpoint.GenerateClientCertificate(request);
        })
        .WithName("Generate Client Certificate")
        .RequireAuthorization("RequireTenantAuth")
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
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(); 

        app.MapGet("/api/client/certificates/flowserver/base64", async (
            [FromServices] CertificateEndpoint endpoint) =>
        {
            var cert = await endpoint.GetFlowServerCertBase64();
            var privateKey = await endpoint.GetFlowServerPrivateKeyBase64();
            return Results.Ok(new {
                apiKey = cert + ":" + privateKey
            });
        })
        .WithName("Get Flow Server Certificate Base64")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(); 

        app.MapGet("/api/client/certificates/flowserver/cert", async (
            [FromQuery] string fileName,
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return await endpoint.GetFlowServerCertFile(fileName);
        })
        .WithName("Get Flow Server Certificate File")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(); 

        app.MapGet("/api/client/certificates/flowserver/privatekey", async (
            [FromQuery] string fileName,
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return await endpoint.GetFlowServerPrivateKeyFile(fileName);
        })
        .WithName("Get Flow Server Private Key File")
        .RequireAuthorization("RequireTenantAuth")
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
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();
    }

    private static void MapInstructionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/instructions/{id}", async (
            string id,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetInstructionById(id);
        })
        .WithName("Get Instruction")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions", async (
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetInstructions();
        })
        .WithName("Get Instructions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapPost("/api/client/instructions", async (
            [FromBody] InstructionRequest request,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.CreateInstruction(request);
        })
        .WithName("Create Instruction")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions/latest/{name}", async (
            string name,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetLatestInstructionByName(name);
        })
        .WithName("Get Latest Instruction")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions/latest", async (
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetLatestInstructions();
        })
        .WithName("Get Latest Instructions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapDelete("/api/client/instructions/{id}", async (
            string id,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.DeleteInstruction(id);
        })
        .WithName("Delete Instruction")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapDelete("/api/client/instructions/all", async (
            [FromBody] DeleteAllVersionsRequest request,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.DeleteAllVersions(request);
        })
        .WithName("Delete All Versions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions/versions", async (
            [FromQuery]string name,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetInstructionVersions(name);
        })
        .WithName("Get Instruction Versions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();
    }
    
    private static void MapWorkflowEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/workflows/{workflowId}/{runId}", async (
            string workflowId,
            string? runId,
            [FromServices] WorkflowFinderEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflow(workflowId, runId);
        })
        .WithName("Get Workflow")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapPost("/api/client/workflows", async (
            [FromBody] WorkflowRequest request,
            [FromServices] WorkflowStarterEndpoint endpoint) =>
        {
            return await endpoint.HandleStartWorkflow(request);
        })
        .WithName("Create New Workflow")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/workflows/{workflowId}/events", async (
            string workflowId,
            [FromServices] WorkflowEventsEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflowEvents(workflowId);
        })
        .WithName("Get Workflow Events")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/workflows", async (
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] string? owner,
            [FromQuery] string? status,
            [FromServices] WorkflowFinderEndpoint endpoint) =>
        {
            return await endpoint.GetWorkflows(startTime, endTime, owner, status);
        })
        .WithName("Get Workflows")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflows with optional filters";
            operation.Description = "Retrieves workflows with optional filtering by date range and owner";
            return operation;
        });

        app.MapPost("/api/client/workflows/{workflowId}/cancel", async (
            string workflowId,
            [FromQuery] bool force,
            [FromServices] WorkflowCancelEndpoint endpoint) =>
        {
            return await endpoint.CancelWorkflow(workflowId, force);
        })
        .WithName("Cancel Workflow")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/workflows/{workflowId}/events/stream", (
            [FromServices] WorkflowEventsEndpoint endpoint,
            string workflowId) =>
        {
            return endpoint.StreamWorkflowEvents(workflowId);
        })
        .WithName("Stream Workflow Events")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Stream workflow events in real-time";
            operation.Description = "Provides a server-sent events (SSE) stream of workflow activity events";
            return operation;
        });
    }
}