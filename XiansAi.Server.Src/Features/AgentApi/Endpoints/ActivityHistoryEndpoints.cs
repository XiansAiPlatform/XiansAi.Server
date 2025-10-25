using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using System.Text.Json;

namespace Features.AgentApi.Endpoints;

// Non-static class for logger type parameter
public class ActivityHistoryEndpointLogger {}

public static class ActivityHistoryEndpoints
{
    private static ILogger<ActivityHistoryEndpointLogger> _logger = null!;

    public static void MapActivityHistoryEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ActivityHistoryEndpointLogger>();
        
        // Map activity history endpoints
        var activityHistoryGroup = app.MapGroup("/api/agent/activity-history")
            .WithTags("AgentAPI - Activity History")
            .RequiresCertificate();
            
        activityHistoryGroup.MapPost("", (
            [FromServices] IActivityHistoryService endpoint,
            [FromBody] ActivityHistoryRequest request,
            HttpContext context) =>
        {
            try
            {
                endpoint.Create(request);
                return Results.Ok("Activity history creation queued");
            }
            catch (JsonException)
            {
                return Results.BadRequest("Invalid JSON format");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating activity history");
                return Results.Problem("Error creating activity history");
            }
        })
        .WithOpenApi(operation => {
            operation.Summary = "Create activity history";
            operation.Description = "Creates a new activity history record in the system";
            return operation;
        });
    }
} 