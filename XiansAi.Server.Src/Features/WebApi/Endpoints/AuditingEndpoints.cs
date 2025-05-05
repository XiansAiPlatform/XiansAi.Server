using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Features.WebApi.Services;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class AuditingEndpoints
{
    public static void MapAuditingEndpoints(this WebApplication app)
    {
        // Map auditing endpoints with common attributes
        var auditingGroup = app.MapGroup("/api/client/auditing")
            .WithTags("WebAPI - Auditing")
            .RequiresValidTenant();

        // Get all participants for a specific agent
        auditingGroup.MapGet("/agents/{agent}/participants", async (
            [FromRoute] string agent, 
            [FromServices] IAuditingService auditingService,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            Console.WriteLine("Getting participants for agent: " + agent);
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            var (participants, totalCount) = await auditingService.GetParticipantsForAgentAsync(agent, page, pageSize);
            
            var result = new
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                Participants = participants
            };
            
            Console.WriteLine("Participants: " + string.Join(", ", participants));

            return Results.Ok(result);
        })
        .WithName("GetAuditingParticipantsForAgent")
        .WithOpenApi(operation => {
            operation.Summary = "Get all participants for a specific agent";
            operation.Description = "Returns a paginated list of all participants associated with the specified agent";
            return operation;
        });

        // Get all workflow types for a specific agent and optional participant
        auditingGroup.MapGet("/agents/{agent}/workflow-types", async (
            [FromRoute] string agent, 
            [FromServices] IAuditingService auditingService,
            [FromQuery] string? participantId) =>
        {
            Console.WriteLine("Getting workflow types for agent with participantId: " + participantId + " and agent: " + agent);
            var workflowTypes = await auditingService.GetWorkflowTypesAsync(agent, participantId);
            Console.WriteLine("Workflow types: " + string.Join(", ", workflowTypes));
            return Results.Ok(workflowTypes);
        })
        .WithName("GetAuditingWorkflowTypes")
        .WithOpenApi(operation => {
            operation.Summary = "Get all workflow types for a specific agent";
            operation.Description = "Returns all workflow types for a specific agent and optional participant";
            return operation;
        });

        // Get workflow IDs for a specific workflow type
        auditingGroup.MapGet("/agents/{agent}/workflow-types/{workflowType}/workflow-ids", async (
            [FromRoute] string agent,
            [FromRoute] string workflowType,
            [FromServices] IAuditingService auditingService,
            [FromQuery] string? participantId) =>
        {
            Console.WriteLine($"Getting workflow IDs for agent: {agent}, workflow type: {workflowType}, and participantId: {participantId}");
            var workflowIds = await auditingService.GetWorkflowIdsForWorkflowTypeAsync(agent, workflowType, participantId);
            Console.WriteLine("Workflow IDs: " + string.Join(", ", workflowIds));
            return Results.Ok(workflowIds);
        })
        .WithName("GetWorkflowIdsForWorkflowType")
        .WithOpenApi(operation => {
            operation.Summary = "Get all workflow IDs for a specific workflow type";
            operation.Description = "Returns all workflow IDs for a specific agent, workflow type, and optional participant";
            return operation;
        });

        // Get logs filtered by agent, participant, workflow type, workflow ID, and log level with pagination
        auditingGroup.MapGet("/logs", async (
            [FromQuery] string agent,
            [FromServices] IAuditingService auditingService,
            [FromQuery] string? participantId = null,
            [FromQuery] string? workflowType = null,
            [FromQuery] string? workflowId = null,
            [FromQuery] LogLevel? logLevel = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            Console.WriteLine($"Getting logs for agent: {agent}, participantId: {participantId}, workflowType: {workflowType}, workflowId: {workflowId}, logLevel: {logLevel}, page: {page}, pageSize: {pageSize}");
            if (string.IsNullOrEmpty(agent))
            {
                return Results.BadRequest("Agent is required");
            }

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            var (logs, totalCount) = await auditingService.GetLogsAsync(
                agent, 
                participantId, 
                workflowType, 
                workflowId,
                logLevel, 
                page, 
                pageSize);

            var result = new
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                Logs = logs
            };

            return Results.Ok(result);
        })
        .WithName("GetAuditingLogs")
        .WithOpenApi(operation => {
            operation.Summary = "Get logs filtered by criteria";
            operation.Description = "Get logs filtered by agent, participant, workflow type, workflow ID, and log level with pagination";
            return operation;
        });
    }
} 