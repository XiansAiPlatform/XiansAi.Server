using Features.AgentApi.Auth;

namespace Features.AgentApi.Endpoints.V2;

// Non-static class for logger type parameter
public class ActivityHistoryEndpointLogger {}

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

        // Reuse v1 mappings
        V1.ActivityHistoryEndpointsV1.CommonMapRoutes(activityHistoryGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(activityHistoryGroup, version);
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        // You can add new routes specific to v2 here if needed
        // For now, we are reusing the v1 mappings
    }
} 