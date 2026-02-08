using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

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
            [FromServices] ILogsService service) =>
        {
            return await service.CreateLogs(requests);
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Create multiple logs";
            operation.Description = "Creates multiple log entries for workflow monitoring";
            return operation;
        });

        logsGroup.MapPost("/single", async (
            [FromBody] LogRequest request,
            [FromServices] ILogsService service) =>
        {
            return await service.CreateLog(request);
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Create single log";
            operation.Description = "Creates a single log entry for workflow monitoring";
            return operation;
        });
    }
} 