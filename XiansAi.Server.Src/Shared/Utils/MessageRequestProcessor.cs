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
            string? origin = null)
        {

            if (messageType == MessageType.Data)
            {
                return new ChatOrDataRequest
                {
                    RequestId = requestId,
                    ParticipantId = participantId,
                    WorkflowId = workflowId,
                    Data = request?.ValueKind == JsonValueKind.Undefined ? null : request,
                    Authorization = null
                };
            }
            else if (messageType == MessageType.Chat)
            {
                // Use text from query parameter if provided, otherwise try to extract from request body
                string? resolvedText = text;
                object? data = null;

                if (string.IsNullOrEmpty(resolvedText) && request.HasValue)
                {
                    resolvedText = ExtractTextFromJsonElement(request.Value);
                }

                // If we have both text (from query) and request body, use request body as data
                if (!string.IsNullOrEmpty(text) && request.HasValue &&
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
                    Authorization = null,
                    Origin = origin
                };
            }
            else
            {
                throw new ArgumentException($"Unsupported message type: {messageType}", nameof(messageType));
            }
        }

        // Backward compatibility methods
        public static ChatOrDataRequest CreateInboundRequest(
            MessageType messageType,
            string workflowId,
            string participantId,
            JsonElement? request = null,
            string? text = null,
            string? requestId = null)
        {
            return CreateRequest(messageType, workflowId, participantId, request, text, requestId);
        }

        public static ChatOrDataRequest CreateSyncRequest(
            MessageType messageType,
            string workflow,
            string participantId,
            string requestId,
            JsonElement? request = null,
            string? text = null)
        {
            return CreateRequest(messageType, workflow, participantId, request, text, requestId);
        }

        public static string GenerateRequestId(string workflow, string participantId)
        {
            return $"{workflow}:{participantId}:{Guid.NewGuid()}";
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