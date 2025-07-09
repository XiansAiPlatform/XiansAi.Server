using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Features.WebApi.Services;
using Features.WebApi.Models;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class AuditingEndpoints
{
    public static void MapAuditingEndpoints(this WebApplication app)
    {
        // Map auditing endpoints with common attributes
        var auditingGroup = app.MapGroup("/api/client/auditing")
            .WithTags("WebAPI - Auditing")
            .RequiresValidTenant()
            .RequireAuthorization();

        // Get all participants for a specific agent
        auditingGroup.MapGet("/agents/{agent}/participants", async (
            [FromRoute] string agent, 
            [FromServices] IAuditingService auditingService,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var participantsResult = await auditingService.GetParticipantsForAgentAsync(agent, page, pageSize);
            
            if (!participantsResult.IsSuccess)
            {
                return participantsResult.ToHttpResult();
            }

            var (participants, totalCount) = participantsResult.Data;
            
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
            var result = await auditingService.GetWorkflowTypesAsync(agent, participantId);
            return result.ToHttpResult();
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
            var result = await auditingService.GetWorkflowIdsForWorkflowTypeAsync(agent, workflowType, participantId);
            return result.ToHttpResult();
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
            var logsResult = await auditingService.GetLogsAsync(
                agent, 
                participantId, 
                workflowType, 
                workflowId,
                logLevel,
                startTime,
                endTime,
                page, 
                pageSize);

            if (!logsResult.IsSuccess)
            {
                return logsResult.ToHttpResult();
            }

            var (logs, totalCount) = logsResult.Data;

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

        // Get critical log of each workflow run across all agents the user has access to
        // Grouped by agent > workflow type > workflow > workflow run > error log
        auditingGroup.MapGet("/critical-logs", async (
            [FromServices] IAuditingService auditingService,
            [FromServices] IAgentService agentService,
            [FromQuery] DateTime? startTime = null,
            [FromQuery] DateTime? endTime = null) =>
        {
            // Get all agents the user has access to
            var agentNamesResult = await agentService.GetAgentNames();
            
            if (!agentNamesResult.IsSuccess || agentNamesResult.Data == null || !agentNamesResult.Data.Any())
            {
                return Results.Ok(new List<AgentCriticalGroup>());
            }

            var groupedCriticalLogsResult = await auditingService.GetGroupedCriticalLogsAsync(
                agentNamesResult.Data,
                startTime,
                endTime);
            
            return groupedCriticalLogsResult.ToHttpResult();
        })
        .WithName("GetLastCriticalLogsByWorkflowRunAcrossAgents")
        .WithOpenApi(operation => {
            operation.Summary = "Get the critical log of each workflow run across all agents";
            operation.Description = "Returns the critical log of each workflow run grouped by agent, workflow type, workflow, and workflow run";
            return operation;
        });
    }
} 