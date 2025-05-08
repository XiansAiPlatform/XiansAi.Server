using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Features.WebApi.Services;
using XiansAi.Server.Features.WebApi.Models;

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
            var workflowTypes = await auditingService.GetWorkflowTypesAsync(agent, participantId);
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
            var workflowIds = await auditingService.GetWorkflowIdsForWorkflowTypeAsync(agent, workflowType, participantId);
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
            [FromQuery] DateTime? startTime = null,
            [FromQuery] DateTime? endTime = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
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
                startTime,
                endTime,
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
            operation.Description = "Get logs filtered by agent, participant, workflow type, workflow ID, log level, and time range with pagination";
            return operation;
        });

        // Get the last error log of each workflow run across all agents the user has access to
        // Grouped by agent > workflow type > workflow > workflow run > error log
        auditingGroup.MapGet("/error-logs", async (
            [FromServices] IAuditingService auditingService,
            [FromServices] IAgentService agentService,
            [FromQuery] DateTime? startTime = null,
            [FromQuery] DateTime? endTime = null) =>
        {
            // Get all agents the user has access to
            var agentNames = await agentService.GetAgentNames();
            
            if (agentNames == null || !agentNames.Any())
            {
                return Results.Ok(new List<AgentErrorGroup>());
            }

            var groupedErrorLogs = await auditingService.GetGroupedErrorLogsAsync(
                agentNames,
                startTime,
                endTime);
            return Results.Ok(groupedErrorLogs);
        })
        .WithName("GetLastErrorLogsByWorkflowRunAcrossAgents")
        .WithOpenApi(operation => {
            operation.Summary = "Get the last error log of each workflow run across all agents";
            operation.Description = "Returns the last error log of each workflow run grouped by agent, workflow type, workflow, and workflow run";
            return operation;
        });
    }
} 