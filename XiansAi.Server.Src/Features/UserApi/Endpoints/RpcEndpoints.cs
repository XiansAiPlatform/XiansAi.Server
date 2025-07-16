using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Features.UserApi.Services;
using Microsoft.OpenApi.Models;

namespace Features.UserApi.Endpoints
{
    public static class RpcEndpoints
    {
        public static void MapRpcEndpoints(this WebApplication app)
        {
            var messagingGroup = app.MapGroup("/api/user/rpc")
                .WithTags("UserAPI - RPC").
                RequireAuthorization("EndpointAuthPolicy");;

            messagingGroup.MapPost("/", async (
                [FromQuery] string workflow,
                [FromQuery] string procedureName,
                [FromBody] JsonElement request,
                [FromServices] IRpcService rpcService) => {

                    if (string.IsNullOrEmpty(workflow) || string.IsNullOrEmpty(procedureName))
                    {
                        return Results.BadRequest("Both 'workflow' and 'procedureName' query parameters are required.");
                    }

                    try
                    {
                        var result = await rpcService.HandleRpcRequest(workflow, procedureName, request);
                        return Results.Ok(result);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest($"Invalid request parameters: {ex.Message}");
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.Problem($"Operation failed: {ex.Message}", statusCode: 500);
                    }
                    catch (TimeoutException ex)
                    {
                        return Results.Problem($"Request timed out: {ex.Message}", statusCode: 408);
                    }
                    catch (Exception ex)
                    {
                        return Results.Problem($"An error occurred while processing RPC request: {ex.Message}", statusCode: 500);
                    }
                })
                .WithName("Send RPC to a Flow")
                .WithOpenApi(operation => {
                    operation.Summary = "Send RPC request to workflow";
                    operation.Description = "Send an RPC request to a workflow. Requires workflow and procedureName as query parameters. Authentication via API key in 'apikey' query parameter or Authorization header.";
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "apikey",
                        In = ParameterLocation.Query,
                        Description = "API key for authentication (sk-Xnai-...)",
                        Required = false,
                        Schema = new OpenApiSchema { Type = "string" }
                    });
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "tenantId",
                        In = ParameterLocation.Query,
                        Description = "Tenant ID (can be extracted from workflow if not provided)",
                        Required = false,
                        Schema = new OpenApiSchema { Type = "string" }
                    });
                    return operation;
                });

        }

    }
}
