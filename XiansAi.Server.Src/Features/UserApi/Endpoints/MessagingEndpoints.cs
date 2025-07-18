using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using Shared.Services;
using Shared.Repositories;
using Shared.Auth;
using System.Text.Json;

namespace Features.UserApi.Endpoints
{
    public static class MessagingEndpoints
    {
        public static void MapMessagingEndpoints(this WebApplication app)
        {
            var messagingGroup = app.MapGroup("/api/user/messaging")
                .WithTags("UserAPI - Messaging")
                .RequireAuthorization("EndpointAuthPolicy");

            messagingGroup.MapPost("/inbound", async (
                [FromQuery] string workflowId,
                [FromQuery] string type,
                [FromQuery] string apikey,
                [FromQuery] string participantId,
                [FromBody] JsonElement request,
                [FromServices] IMessageService messageService,
                [FromServices] ITenantContext tenantContext,
                HttpContext context) => {
                    var messageTypeEnum = Enum.Parse<MessageType>(type);
                    if (!Enum.IsDefined(typeof(MessageType), messageTypeEnum))
                    {
                        return Results.BadRequest("Invalid message type specified.");
                    }
                    if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(apikey))
                    {
                        return Results.BadRequest("WorkflowId and apikey are required.");
                    }

                    var resolvedParticipantId = string.IsNullOrEmpty(participantId) ? tenantContext.LoggedInUser : participantId;

                    if (messageTypeEnum == MessageType.Data)
                    {
                        // Accept any JSON object or value for Data
                        var chat = new ChatOrDataRequest
                        {
                            ParticipantId = resolvedParticipantId,
                            WorkflowId = workflowId,
                            Data = request.ValueKind == JsonValueKind.Undefined ? null : request,
                            Authorization = apikey
                        };
                        var result = await messageService.ProcessIncomingMessage(chat, MessageType.Data);
                        return result.ToHttpResult();
                    }
                    else if (messageTypeEnum == MessageType.Chat)
                    {
                        // Accept string or extract string from JSON
                        string? text = null;
                        if (request.ValueKind == JsonValueKind.String)
                        {
                            text = request.GetString();
                        }
                        else if (request.ValueKind != JsonValueKind.Undefined && request.ValueKind != JsonValueKind.Null)
                        {
                            // If not a string, try to get the raw JSON as string
                            text = request.ToString();
                        }
                        var chat = new ChatOrDataRequest
                        {
                            ParticipantId = resolvedParticipantId,
                            WorkflowId = workflowId,
                            Text = text,
                            Authorization = apikey
                        };
                        var result = await messageService.ProcessIncomingMessage(chat, MessageType.Chat);
                        return result.ToHttpResult();
                    }
                    else
                    {
                        return Results.BadRequest("Invalid message type specified.");
                    }
                })
                .WithName("Send Data to workflow from user api")
                .WithOpenApi(operation => {
                    operation.Summary = "Send Data to workflow";
                    operation.Description = "Send a data to a workflow. Requires workflowId, type, participantId and apikey as query parameters.";
                    return operation;
                });

            // New synchronous endpoint that waits for responses
            messagingGroup.MapPost("/inbound/sync", async (
                [FromQuery] string workflow,
                [FromQuery] string type,
                [FromQuery] string apikey,
                [FromQuery] string participantId,
                [FromBody] JsonElement? request,
                [FromServices] IMessageService messageService,  
                [FromServices] ITenantContext tenantContext,
                [FromServices] IPendingRequestService pendingRequestService,
                HttpContext context,
                [FromQuery] int timeoutSeconds = 60,
                [FromQuery] string? requestId = null,
                [FromQuery] string? text = null) => {
                    var messageTypeEnum = Enum.Parse<MessageType>(type);
                    if (!Enum.IsDefined(typeof(MessageType), messageTypeEnum))
                    {
                        return Results.BadRequest("Invalid message type specified.");
                    }
                    if (string.IsNullOrEmpty(workflow) || string.IsNullOrEmpty(apikey))
                    {
                        return Results.BadRequest("WorkflowId and apikey are required.");
                    }

                    if (timeoutSeconds < 1 || timeoutSeconds > 300) // Max 5 minutes
                    {
                        return Results.BadRequest("Timeout must be between 1 and 300 seconds.");
                    }

                    var workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;

                    var resolvedParticipantId = string.IsNullOrEmpty(participantId) ? tenantContext.LoggedInUser : participantId;
                    
                    // Generate unique request ID for correlation
                    if (string.IsNullOrEmpty(requestId))
                    {
                        requestId = $"{workflow}:{resolvedParticipantId}:{Guid.NewGuid()}";
                    }

                    try
                    {
                        ChatOrDataRequest chat;
                        
                        if (messageTypeEnum == MessageType.Data)
                        {
                            chat = new ChatOrDataRequest
                            {
                                RequestId = requestId,
                                ParticipantId = resolvedParticipantId,
                                WorkflowId = workflow,
                                Data = request,
                                Authorization = apikey
                            };
                        }
                        else if (messageTypeEnum == MessageType.Chat)
                        {
                            // Use text from query parameter if provided, otherwise try to extract from request body
                            if (string.IsNullOrEmpty(text))
                            {
                                if (request.HasValue)
                                {
                                    if (request.Value.ValueKind == JsonValueKind.String)
                                    {
                                        text = request.Value.GetString();
                                    }
                                    else if (request.Value.ValueKind != JsonValueKind.Undefined && request.Value.ValueKind != JsonValueKind.Null)
                                    {
                                        // If not a string, try to get the raw JSON as string
                                        text = request.Value.ToString();
                                    }
                                }
                            }
                            
                            // If we have both text (from query) and request body, use request body as data
                            object? data = null;
                            if (!string.IsNullOrEmpty(text) && request.HasValue && 
                                request.Value.ValueKind != JsonValueKind.Undefined && 
                                request.Value.ValueKind != JsonValueKind.Null)
                            {
                                data = request.Value;
                            }
                            
                            chat = new ChatOrDataRequest
                            {
                                RequestId = requestId,
                                ParticipantId = resolvedParticipantId,
                                WorkflowId = workflow,
                                Text = text,
                                Data = data,
                                Authorization = apikey
                            };
                        }
                        else
                        {
                            return Results.BadRequest("Invalid message type specified.");
                        }

                        // Start waiting for the response (this sets up the TaskCompletionSource)
                        var responseTask = pendingRequestService.WaitForResponseAsync<ConversationMessage>(
                            requestId, 
                            TimeSpan.FromSeconds(timeoutSeconds), 
                            messageTypeEnum,
                            context.RequestAborted);

                        // Process the incoming message asynchronously (using existing flow)
                        var processResult = await messageService.ProcessIncomingMessage(chat, messageTypeEnum);
                        
                        if (!processResult.IsSuccess)
                        {
                            pendingRequestService.CancelRequest(requestId);
                            return processResult.ToHttpResult();
                        }

                        // Wait for the response from the change stream
                        var response = await responseTask;
                        
                        if (response == null)
                        {
                            return Results.Problem("No response received within timeout period", statusCode: 408);
                        }

                        // Return the response message
                        return Results.Ok(new
                        {
                            RequestId = requestId,
                            ThreadId = processResult.Data,
                            Response = new
                            {
                                response.Id,
                                response.Text,
                                response.Data,
                                response.CreatedAt,
                                response.Direction,
                                response.MessageType,
                                response.Scope,
                                response.Hint
                            }
                        });
                    }
                    catch (TimeoutException)
                    {
                        return Results.Problem("Request timed out waiting for response", statusCode: 408);
                    }
                    catch (OperationCanceledException)
                    {
                        pendingRequestService.CancelRequest(requestId);
                        return Results.Problem("Request was cancelled", statusCode: 499);
                    }
                    catch (Exception ex)
                    {
                        pendingRequestService.CancelRequest(requestId);
                        return Results.Problem($"An error occurred: {ex.Message}", statusCode: 500);
                    }
                })
                .WithName("Send Data to workflow and wait for response")
                .WithOpenApi(operation => {
                    operation.Summary = "Send Data to workflow and wait for response";
                    operation.Description = "Send data to a workflow and wait synchronously for the response. Requires workflowId, type, participantId and apikey as query parameters. For Chat messages, you can pass text as query parameter and optional JSON data in request body. For Data messages, pass JSON in request body. Optional timeoutSeconds (default: 60, max: 300).";
                    return operation;
                });

            // History endpoint with caching
            messagingGroup.MapGet("/history/{workflow}/{participantId}", async (
                [FromRoute] string workflow,
                [FromRoute] string participantId,
                [FromServices] IMessageService messageService,
                [FromServices] ITenantContext tenantContext,
                [FromQuery] int page = 1,
                [FromQuery] int pageSize = 50,
                [FromQuery] string? scope = null) =>
            {
                var workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;
                var result = await messageService.GetThreadHistoryAsync(workflowId, participantId, page, pageSize, scope);
                return result.ToHttpResult();
            })
            .WithName("GetMessageHistory")
            .WithSummary("Get conversation history with caching")
            .WithDescription("Retrieves conversation history with optimized database queries and caching");
        }
    }
}
