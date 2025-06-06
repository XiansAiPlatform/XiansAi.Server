using Features.AgentApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Services.Lib;
using System.Text.Json;

//Boilerplate code for future versions

namespace Features.AgentApi.Endpoints.V2;

// Non-static class for logger type parameter
public class ActivityHistoryEndpointLogger { }

public static class ActivityHistoryEndpointsV2
{
    private static ILogger<ActivityHistoryEndpointLogger> _logger = null!;

    public static void MapActivityHistoryEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v2";
        _logger = loggerFactory.CreateLogger<ActivityHistoryEndpointLogger>();

        // Map activity history endpoints
        var activityHistoryGroup = app.MapGroup($"/api/{version}/agent/activity-history")
            .WithTags($"AgentAPI - Activity History {version}")
            .RequiresCertificate();

        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reuse v1 mappings
        MapRoutes(activityHistoryGroup, version, registeredPaths);
        V1.ActivityHistoryEndpointsV1.MapRoutes(activityHistoryGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        string RouteKey(string method, string path) => $"{method}:{path}";

        // If v2 has the same endpoint with changes, we can overwrite it, before v1 is called this method will be called and hashset will record that it is already called
        // Hence v1 would not register the same endpoint again

        var historyPath = "/";
        if (registeredPaths.Add(RouteKey("POST", historyPath)))
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
            .WithOpenApi(operation =>
            {
                operation.Summary = "Create activity history";
                operation.Description = "Creates a new activity history record in the system";
                return operation;
            });
        }


        // If there deprecated routes in v2 which are in v1 but would not be in v2, overwrite them.
        // Example of a deprecated route that might be removed in future versions

        // if (registeredPaths.Add(RouteKey("POST", "/old-endpoint")))
        // {
        //     group.MapPost("/old-endpoint", async (
        //     OldRequestDto request,
        //     [FromServices] ISomeService service,
        //     HttpContext context) =>
        //     {
        //         context.Response.Headers.Add("X-Deprecated", "This endpoint is deprecated and will be removed in v2.");
        //         var result = await service.HandleAsync(request);
        //         return Results.Ok(result);
        //     })
        //     .WithName($"{version} - OldEndpoint")
        //     .WithDescription("Deprecated: This endpoint will be removed in the next version.");
        // }

    }
} 