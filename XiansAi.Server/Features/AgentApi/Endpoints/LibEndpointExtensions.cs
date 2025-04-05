using Features.AgentApi.Services.Agent;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using System.Text.Json;

namespace Features.AgentApi.Endpoints;
public static class LibEndpointExtensions
{
    public static void MapLibEndpoints(this WebApplication app)
    {
        MapObjectCacheEndpoints(app);
        MapServerEndpoints(app);
    }

    private static void MapObjectCacheEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/cache/{key}", async (
            string key,
            [FromServices] ObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.GetValue(key);
        })
        .WithName("Get Cache Value")
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Get a value from cache";
            operation.Description = "Retrieves a value from the cache by its key";
            return operation;
        });

        app.MapPost("/api/client/cache/{key}", async (
            string key,
            [FromBody] JsonElement value,
            [FromServices] ObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.SetValue(key, value);
        })
        .WithName("Set Cache Value")
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Set a value in cache";
            operation.Description = "Stores a value in the cache with the specified key";
            return operation;
        });

        app.MapDelete("/api/client/cache/{key}", async (
            string key,
            [FromServices] ObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.DeleteValue(key);
        })
        .WithName("Delete Cache Value")
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Delete a value from cache";
            operation.Description = "Removes a value from the cache by its key";
            return operation;
        });
    }
    private static void MapServerEndpoints(this WebApplication app)
    {
        app.MapGet("/api/server/instructions/latest", async (
            [FromQuery] string name,
            [FromServices] InstructionsService endpoint) =>
        {
            var result = await endpoint.GetLatestInstruction(name);
            return result;
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Get latest instructions";
            operation.Description = "Gets the latest instructions";
            return operation;
        });

        app.MapPost("/api/server/activities", async (
            [FromBody] ActivityRequest request,
            [FromServices] ActivityHistoryService endpoint) =>
        {
            await endpoint.CreateAsync(request);
            return Results.Ok();
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Create activity";
            operation.Description = "Creates a new activity record";
            return operation;
        });

        app.MapPost("/api/server/activities/endtime", async (
            [FromBody] ActivityEndTimeRequest request,
            [FromServices] ActivityHistoryService endpoint) =>
        {
            return await endpoint.UpdateEndTimeAsync(request);
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Update activity end time";
            operation.Description = "Updates the end time and result of an activity";
            return operation;
        });

        app.MapGet("/api/server/activities/workflow/{workflowId}", async (
            string workflowId,
            [FromServices] ActivityHistoryService endpoint) =>
        {
            return await endpoint.GetByWorkflowIdAsync(workflowId);
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Get activities by workflow ID";
            operation.Description = "Retrieves all activities for a specific workflow";
            return operation;
        });

        app.MapGet("/api/server/activities/workflow/{workflowId}/activity/{activityId}", async (
            string workflowId,
            string activityId,
            [FromServices] ActivityHistoryService endpoint) =>
        {
            return await endpoint.GetActivityAsync(workflowId, activityId);
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Get specific activity";
            operation.Description = "Retrieves a specific activity by workflow ID and activity ID";
            return operation;
        });

        app.MapPost("/api/server/definitions", async (
            [FromBody] FlowDefinitionRequest request,
            [FromServices] DefinitionsService endpoint) =>
        {
            return await endpoint.CreateAsync(request);
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Create definitions";
            operation.Description = "Creates definitions";
            return operation;
        });
    }
}