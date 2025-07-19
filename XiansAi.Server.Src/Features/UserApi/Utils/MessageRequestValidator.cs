using Shared.Utils.Services;
using Shared.Repositories;

namespace Features.UserApi.Utils
{
    public static class MessageRequestValidator
    {
        public static (bool IsValid, string? ErrorMessage) ValidateInboundRequest(
            string? workflow, 
            string? type, 
            string? apikey,
            out MessageType messageType)
        {
            messageType = default;

            if (string.IsNullOrEmpty(workflow) || string.IsNullOrEmpty(apikey))
            {
                return (false, "WorkflowId and apikey are required.");
            }

            if (string.IsNullOrEmpty(type))
            {
                return (false, "Message type is required.");
            }

            if (!Enum.TryParse<MessageType>(type, out messageType) || !Enum.IsDefined(typeof(MessageType), messageType))
            {
                return (false, "Invalid message type specified.");
            }

            return (true, null);
        }

        public static (bool IsValid, string? ErrorMessage) ValidateSyncRequest(
            string? workflow, 
            string? type, 
            string? apikey,
            int timeoutSeconds,
            out MessageType messageType)
        {
            messageType = default;

            if (string.IsNullOrEmpty(workflow) || string.IsNullOrEmpty(apikey))
            {
                return (false, "WorkflowId and apikey are required.");
            }

            if (string.IsNullOrEmpty(type))
            {
                return (false, "Message type is required.");
            }

            if (!Enum.TryParse<MessageType>(type, out messageType) || !Enum.IsDefined(typeof(MessageType), messageType))
            {
                return (false, "Invalid message type specified.");
            }

            if (timeoutSeconds < 1 || timeoutSeconds > 300) // Max 5 minutes
            {
                return (false, "Timeout must be between 1 and 300 seconds.");
            }

            return (true, null);
        }
    }
} 