using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.AgentApi.Auth;

namespace Features.AgentApi.Endpoints;

public static class LogsEndpoints
{
    public static void MapLogsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<AgentEndpointLogger>();
        
        // Map logs endpoints
        var logsGroup = app.MapGroup("/api/agent/logs")
            .WithTags("AgentAPI - Logs")
            .RequiresCertificate();

        logsGroup.MapPost("/", async (
            [FromBody] LogRequest[] requests,
            [FromServices] LogsService service) =>
        {
            return await service.CreateLogs(requests);
        })
        .WithOpenApi(operation => {
            operation.Summary = "Create multiple logs";
            operation.Description = "Creates multiple log entries for workflow monitoring";
            return operation;
        });

        logsGroup.MapPost("/single", async (
            [FromBody] LogRequest request,
            [FromServices] LogsService service) =>
        {
            return await service.CreateLog(request);
        })
        .WithOpenApi(operation => {
            operation.Summary = "Create single log";
            operation.Description = "Creates a single log entry for workflow monitoring";
            return operation;
        });
    }
} 