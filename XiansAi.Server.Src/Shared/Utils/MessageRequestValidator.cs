using Shared.Repositories;

namespace Shared.Utils
{
    public static class MessageRequestValidator
    {
        public static (bool IsValid, string? ErrorMessage) ValidateInboundRequest(
            string? workflow,
            string? type,
            out MessageType messageType)
        {
            messageType = default;

            if (string.IsNullOrEmpty(workflow))
            {
                return (false, "WorkflowId is required.");
            }

            if (string.IsNullOrEmpty(type))
            {
                return (false, "Message type is required.");
            }

            if (!Enum.TryParse(type, out messageType) || !Enum.IsDefined(typeof(MessageType), messageType))
            {
                return (false, "Invalid message type specified.");
            }

            return (true, null);
        }

        public static (bool IsValid, string? ErrorMessage) ValidateSyncRequest(
            string? workflow,
            string? type,
            int timeoutSeconds,
            out MessageType messageType)
        {
            messageType = default;

            if (string.IsNullOrEmpty(workflow))
            {
                return (false, "Workflow is required.");
            }

            if (string.IsNullOrEmpty(type))
            {
                return (false, "Message type is required.");
            }

            if (!Enum.TryParse(type, out messageType) || !Enum.IsDefined(typeof(MessageType), messageType))
            {
                return (false, "Invalid message type specified.");
            }

            if (timeoutSeconds < 1 || timeoutSeconds > 600) // Max 10 minutes
            {
                return (false, "Timeout must be between 1 and 600 seconds.");
            }

            return (true, null);
        }
    }
}