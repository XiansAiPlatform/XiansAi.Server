using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class ActivityEndpointExtensions
{
    public static void MapActivityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/workflows/{workflowId}/activities/{activityId}", async (
            string workflowId,
            string activityId,
            [FromServices] ActivitiesService endpoint) =>
        {
            return await endpoint.GetActivity(workflowId, activityId);
        }).WithName("Get Activity of Workflow")
        .RequiresValidTenant()
        .WithOpenApi();
    }
} 