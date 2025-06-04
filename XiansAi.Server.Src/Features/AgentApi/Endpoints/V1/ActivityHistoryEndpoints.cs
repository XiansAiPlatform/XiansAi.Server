using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using System.Text.Json;

namespace Features.AgentApi.Endpoints.V1;

// Non-static class for logger type parameter
public class ActivityHistoryEndpointLogger {}

public static class ActivityHistoryEndpointsV1
{
    private static ILogger<ActivityHistoryEndpointLogger> _logger = null!;

    public static void MapActivityHistoryEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v1";
        _logger = loggerFactory.CreateLogger<ActivityHistoryEndpointLogger>();
        
        // Map activity history endpoints
        var activityHistoryGroup = app.MapGroup($"/api/{version}/agent/activity-history")
            .WithTags($"AgentAPI - Activity History {version}")
            .RequiresCertificate();

        // If there are any routes that are common for multiple versions, add them here
        CommonMapRoutes(activityHistoryGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(activityHistoryGroup, version);
    }

    internal static void CommonMapRoutes(RouteGroupBuilder group, string version)
    {
        // If there are any routes that are common for multiple versions, add them here
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        group.MapPost("", (
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
                return Results.Problem($"Error creating activity history: {ex.Message}");
            }
        })
        .WithOpenApi(operation => {
            operation.Summary = "Create activity history";
            operation.Description = "Creates a new activity history record in the system";
            return operation;
        });
    }
} 