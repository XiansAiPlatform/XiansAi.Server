using System.Text.Json;
using Shared.Services;
using Shared.Repositories;

namespace Shared.Utils
{
    public static class MessageRequestProcessor
    {
        public static ChatOrDataRequest CreateRequest(
            MessageType messageType,
            string workflowId,
            string participantId,
            JsonElement? request = null,
            string? text = null,
            string? requestId = null,
            string? origin = null,
            string? authorization = null)
        {

            if (messageType == MessageType.Data)
            {
                return new ChatOrDataRequest
                {
                    RequestId = requestId,
                    ParticipantId = participantId,
                    WorkflowId = workflowId,
                    Text = text,
                    Data = request?.ValueKind == JsonValueKind.Undefined ? null : request,
                    Authorization = authorization,
                    Origin = origin
                };
            }
            else if (messageType == MessageType.Chat)
            {
                // Use text from query parameter
                string? resolvedText = text;
                object? data = null;

                // Use request body as data
                if (request.HasValue &&
                    request.Value.ValueKind != JsonValueKind.Undefined &&
                    request.Value.ValueKind != JsonValueKind.Null)
                {
                    data = request.Value;
                }

                return new ChatOrDataRequest
                {
                    RequestId = requestId,
                    ParticipantId = participantId,
                    WorkflowId = workflowId,
                    Text = resolvedText,
                    Data = data,
                    Authorization = authorization,
                    Origin = origin
                };
            }
            else if (messageType == MessageType.Webhook)
            {
                // Webhook messages are similar to Data messages but with explicit webhook origin
                return new ChatOrDataRequest
                {
                    RequestId = requestId,
                    ParticipantId = participantId,
                    WorkflowId = workflowId,
                    Text = text,
                    Data = request?.ValueKind == JsonValueKind.Undefined ? null : request,
                    Authorization = authorization,
                    Origin = origin ?? "webhook"
                };
            }
            else
            {
                throw new ArgumentException($"Unsupported message type: {messageType}", nameof(messageType));
            }
        }

        public static string GenerateRequestId(string workflow, string participantId)
        {
            return $"{Guid.NewGuid()}";
        }

        private static string? ExtractTextFromJsonElement(JsonElement request)
        {
            if (request.ValueKind == JsonValueKind.String)
            {
                return request.GetString();
            }
            else if (request.ValueKind != JsonValueKind.Undefined && request.ValueKind != JsonValueKind.Null)
            {
                // If not a string, try to get the raw JSON as string
                return request.ToString();
            }

            return null;
        }
    }
}