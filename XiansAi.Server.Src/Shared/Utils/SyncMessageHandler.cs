using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Shared.Utils
{
    public class SyncMessageHandler
    {
        private readonly IMessageService _messageService;
        private readonly IPendingRequestService _pendingRequestService;

        public SyncMessageHandler(IMessageService messageService, IPendingRequestService pendingRequestService)
        {
            _messageService = messageService;
            _pendingRequestService = pendingRequestService;
        }

        public async Task<IResult> ProcessSyncMessageAsync(
            ChatOrDataRequest chatRequest,
            MessageType messageType,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(chatRequest.RequestId))
            {
                throw new ArgumentException("RequestId is required for sync messages", nameof(chatRequest));
            }

            try
            {
                // Start waiting for the response (this sets up the TaskCompletionSource)
                var responseTask = _pendingRequestService.WaitForResponseAsync<ConversationMessage>(
                    chatRequest.RequestId,
                    TimeSpan.FromSeconds(timeoutSeconds),
                    messageType,
                    cancellationToken);

                // Process the incoming message asynchronously (using existing flow)
                var processResult = await _messageService.ProcessIncomingMessage(chatRequest, messageType);

                if (!processResult.IsSuccess)
                {
                    _pendingRequestService.CancelRequest(chatRequest.RequestId);
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
                    chatRequest.RequestId,
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
                _pendingRequestService.CancelRequest(chatRequest.RequestId);
                return Results.Problem("Request was cancelled", statusCode: 499);
            }
            catch (Exception ex)
            {
                _pendingRequestService.CancelRequest(chatRequest.RequestId);
                return Results.Problem($"An error occurred: {ex.Message}", statusCode: 500);
            }
        }
    }
}