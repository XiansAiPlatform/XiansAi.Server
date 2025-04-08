using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services.Web;
using System.Text.Json;

namespace Features.WebApi.Endpoints;
public static class WebEndpointExtensions
{
    public static void MapWebEndpoints(this WebApplication app)
    {
        MapWorkflowEndpoints(app);
        MapInstructionEndpoints(app);
        MapLogsEndpoints(app);
        MapActivityEndpoints(app);
        MapSettingsEndpoints(app);
        MapDefinitionsEndpoints(app);
        MapTenantEndpoints(app);
    }

    private static void MapDefinitionsEndpoints(this WebApplication app)
    {
        app.MapDelete("/api/client/definitions/{definitionId}", async (
            string definitionId,
            [FromServices] DefinitionsEndpoint endpoint) =>
        {
            return await endpoint.DeleteDefinition(definitionId);
        })
        .WithName("Delete Definition")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Delete a specific definition";
            operation.Description = "Removes a definition by its ID";
            return operation;
        });

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
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get definitions with optional filters";
            operation.Description = "Retrieves definitions with optional filtering by date range and owner";
            return operation;
        });
    }

    private static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/client/certificates/generate/base64", (
            HttpContext context,
            [FromBody] CertRequest request,
            [FromServices] CertificateEndpoint endpoint) =>
        {
            return endpoint.GenerateClientCertificateBase64(request);
        })
        .WithName("Generate Client Certificate Base64")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation =>
        {
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
        .WithOpenApi(operation =>
        {
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

        app.MapGet("/api/client/certificates/flowserver/base64", (
            [FromServices] CertificateEndpoint endpoint) =>
        {
            var cert = endpoint.GetFlowServerCertBase64();
            var privateKey = endpoint.GetFlowServerPrivateKeyBase64();
            return Results.Ok(new {
                apiKey = cert + ":" + privateKey
            });
        })
        .WithName("Get Flow Server Certificate Base64")
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
            [FromQuery] string name,
            [FromServices] InstructionsEndpoint endpoint) =>
        {
            return await endpoint.GetInstructionVersions(name);
        })
        .WithName("Get Instruction Versions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();
    }


    public static void MapLogsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/logs/{id}", async (
            string id,
            [FromServices] LogsEndpoint endpoint) =>
        {
            return await endpoint.GetLogById(id);
        })
        .WithName("Get Log by ID")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/logs/by-run-id/{runId}", async (
            string runId,
            [FromServices] LogsEndpoint endpoint) =>
        {
            return await endpoint.GetLogsByRunId(runId);
        })
        .WithName("Get Logs by Run ID")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/logs/by-date-range", async (
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromServices] LogsEndpoint endpoint) =>
        {
            return await endpoint.GetLogsByDateRange(startDate, endDate);
        })
        .WithName("Get Logs by Date Range")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapPost("/api/client/logs", async (
            [FromBody] Log log,
            [FromServices] LogsEndpoint endpoint) =>
        {
            return await endpoint.CreateLog(log);
        })
        .WithName("Create Log")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapDelete("/api/client/logs/{id}", async (
            string id,
            [FromServices] LogsEndpoint endpoint) =>
        {
            return await endpoint.DeleteLog(id);
        })
        .WithName("Delete Log")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/logs/search", async (
            [FromQuery] string searchTerm,
            [FromServices] LogsEndpoint endpoint) =>
        {
            return await endpoint.SearchLogs(searchTerm);
        })
        .WithName("Search Logs")
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
        .WithOpenApi(operation =>
        {
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
        .WithOpenApi(operation =>
        {
            operation.Summary = "Stream workflow events in real-time";
            operation.Description = "Provides a server-sent events (SSE) stream of workflow activity events";
            return operation;
        });
    }

    private static void MapTenantEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/tenants", async (
            [FromServices] TenantEndpoint endpoint) =>
        {
            return await endpoint.GetTenants();
        })
        .WithName("Get Tenants")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get all tenants";
            operation.Description = "Retrieves all tenant records";
            return operation;
        });

        app.MapGet("/api/client/tenants/{id}", async (
            string id,
            [FromServices] TenantEndpoint endpoint) =>
        {
            return await endpoint.GetTenant(id);
        })
        .WithName("Get Tenant")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by ID";
            operation.Description = "Retrieves a tenant by its unique ID";
            return operation;
        });

        app.MapGet("/api/client/tenants/by-tenant-id/{tenantId}", async (
            string tenantId,
            [FromServices] TenantEndpoint endpoint) =>
        {
            return await endpoint.GetTenantByTenantId(tenantId);
        })
        .WithName("Get Tenant By TenantId")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by tenant ID";
            operation.Description = "Retrieves a tenant by its tenant ID";
            return operation;
        });

        app.MapGet("/api/client/tenants/by-domain/{domain}", async (
            string domain,
            [FromServices] TenantEndpoint endpoint) =>
        {
            return await endpoint.GetTenantByDomain(domain);
        })
        .WithName("Get Tenant By Domain")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get tenant by domain";
            operation.Description = "Retrieves a tenant by its domain";
            return operation;
        });

        app.MapPost("/api/client/tenants", async (
            [FromBody] CreateTenantRequest request,
            HttpContext httpContext,
            [FromServices] TenantEndpoint endpoint) =>
        {
            var userIdClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            var createdBy = userIdClaim ?? "system";
            return await endpoint.CreateTenant(request, createdBy);
        })
        .WithName("Create Tenant")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Create a new tenant";
            operation.Description = "Creates a new tenant record";
            return operation;
        });

        app.MapPut("/api/client/tenants/{id}", async (
            string id,
            [FromBody] UpdateTenantRequest request,
            [FromServices] TenantEndpoint endpoint) =>
        {
            return await endpoint.UpdateTenant(id, request);
        })
        .WithName("Update Tenant")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Update a tenant";
            operation.Description = "Updates an existing tenant record";
            return operation;
        });

        app.MapDelete("/api/client/tenants/{id}", async (
            string id,
            [FromServices] TenantEndpoint endpoint) =>
        {
            return await endpoint.DeleteTenant(id);
        })
        .WithName("Delete Tenant")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a tenant";
            operation.Description = "Deletes a tenant by its ID";
            return operation;
        });
    }
}