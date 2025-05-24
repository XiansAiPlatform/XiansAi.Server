using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/client/workflows/{workflowId}/activities")
            .WithTags("WebAPI - Activities")
            .RequiresValidTenant();

        group.MapGet("/{activityId}", async (
            string workflowId,
            string activityId,
            [FromServices] IActivitiesService endpoint) =>
        {
            var result = await endpoint.GetActivity(workflowId, activityId);
            return result.ToHttpResult();
        }).WithName("Get Activity of Workflow")
        .RequiresValidTenant()
        .WithOpenApi();
    }
} 