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

        }
    }
}
